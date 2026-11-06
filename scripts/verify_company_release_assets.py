"""Verify the frozen company-asset custody manifest without network access or authority claims."""
from __future__ import annotations

import base64
import binascii
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import xml.etree.ElementTree as ET

MANIFEST = "docs/migration/company-release-assets.v1.json"
MANIFEST_SHA256 = "11085516263f2590df57a55ff608e63b521b40f2e199715dd2ca4ed5740096be"
SOURCE_COMMITS = frozenset((
    "f981dce7682606b1c13f04b271163447f3f2709e",
    "3c052083d04a46bf36a7e19695ffc5d339300c86",
))
MAX_ASSET_BYTES = 8 * 1024 * 1024
MAX_TOTAL_BYTES = 16 * 1024 * 1024
SVG_NS = "http://www.w3.org/2000/svg"
ENTRY_KEYS = frozenset(("sourceCommit", "sourcePath", "sourceBlob", "targetPath", "bytes", "sha256"))
ASSET_NAME = re.compile(r"(?:customers/[a-z0-9][a-z0-9-]*\.(?:svg|png|jpg|webp|gif)|landing/[a-z0-9][a-z0-9-]*\.webp)")


class AssetError(RuntimeError):
    """Expose fixed categories only; never serialize untrusted source/file content."""


def require(condition, category):
    if not condition:
        raise AssetError(category)


def no_duplicates(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "manifest-duplicate-key")
        result[key] = value
    return result


def parse_manifest(raw):
    try:
        return json.loads(raw.decode("utf-8", "strict"), object_pairs_hook=no_duplicates)
    except (UnicodeError, ValueError):
        raise AssetError("manifest-json") from None


def validate_manifest(manifest):
    keys = {"schema", "sourceRepository", "targetRepository", "targetBase", "scope",
            "sourceCommits", "sourceManifests", "provenance", "assets"}
    require(type(manifest) is dict and set(manifest) == keys
            and manifest["schema"] == "maliev-company-release-assets.v1"
            and manifest["sourceRepository"] == "MALIEV-Co-Ltd/maliev-web"
            and manifest["targetRepository"] == "MALIEV-Co-Ltd/Legacy.Maliev.Web"
            and type(manifest["assets"]) is list and len(manifest["assets"]) == 59,
            "manifest-shape")
    return manifest["assets"]


def asset_relative(entry):
    require(type(entry) is dict and set(entry) == ENTRY_KEYS
            and type(entry["sourceCommit"]) is str and entry["sourceCommit"] in SOURCE_COMMITS
            and type(entry["sourceBlob"]) is str and re.fullmatch(r"[0-9a-f]{40}", entry["sourceBlob"])
            and type(entry["sha256"]) is str and re.fullmatch(r"[0-9a-f]{64}", entry["sha256"])
            and type(entry["bytes"]) is int and 0 < entry["bytes"] <= MAX_ASSET_BYTES,
            "asset-entry")
    source, target = entry["sourcePath"], entry["targetPath"]
    prefix = "Maliev.Web/wwwroot/src/images/"
    require(type(source) is str and type(target) is str and source.startswith(prefix), "asset-path")
    suffix = source[len(prefix):]
    require(ASSET_NAME.fullmatch(suffix) is not None
            and target == "Legacy.Maliev.Web/wwwroot/src/images/" + suffix, "asset-path")
    return target


def read_asset(root, relative, maximum=MAX_ASSET_BYTES):
    root = Path(root)
    try:
        require(root.is_absolute() and root.is_dir() and not root.is_symlink(), "asset-path")
        current = root
        for part in Path(relative).parts:
            require(part not in (".", "..") and not Path(relative).is_absolute(), "asset-path")
            current = current / part
            info = current.lstat()
            require(not stat.S_ISLNK(info.st_mode)
                    and not getattr(info, "st_file_attributes", 0) & 1024, "asset-link")
        require(stat.S_ISREG(info.st_mode) and 0 < info.st_size <= maximum, "asset-read")
        require(current.resolve().is_relative_to(root.resolve()), "asset-path")
        before = current.stat()
        descriptor = os.open(current, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
        with os.fdopen(descriptor, "rb") as source:
            opened = os.fstat(source.fileno())
            require((opened.st_dev, opened.st_ino, opened.st_size, opened.st_mtime_ns)
                    == (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns), "asset-race")
            data = source.read(maximum + 1)
            after = os.fstat(source.fileno())
        final = current.stat()
        require(len(data) == before.st_size
                and (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns)
                == (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns)
                == (final.st_dev, final.st_ino, final.st_size, final.st_mtime_ns), "asset-race")
        return data
    except OSError:
        raise AssetError("asset-read") from None


def safe_css(value):
    require("\\" not in value
            and not re.search(r"@\s*import|expression\s*\(|javascript\s*:", value, re.I), "svg-unsafe")
    for found in re.finditer(r"url\s*\(([^)]*)\)", value, re.I):
        require(re.fullmatch(r"""['"]?\#[A-Za-z_][A-Za-z0-9_.:-]*['"]?""", found.group(1).strip()) is not None,
                "svg-unsafe")


def safe_href(value):
    if re.fullmatch(r"#[A-Za-z_][A-Za-z0-9_.:-]*", value):
        return
    match = re.fullmatch(r"data:image/(png|jpeg);base64,([A-Za-z0-9+/=\s]+)", value)
    require(match is not None, "svg-unsafe")
    try:
        image = base64.b64decode(re.sub(r"\s", "", match[2]), validate=True)
    except (ValueError, binascii.Error):
        raise AssetError("svg-unsafe") from None
    require((match[1] == "png" and image.startswith(b"\x89PNG\r\n\x1a\n"))
            or (match[1] == "jpeg" and image.startswith(b"\xff\xd8\xff")), "svg-unsafe")


def verify_svg(raw):
    require(b"\0" not in raw, "svg-encoding")
    try:
        text = raw.decode("utf-8-sig", "strict")
    except UnicodeError:
        raise AssetError("svg-encoding") from None
    require("\ufeff" not in text and not re.search(r"<!\s*(?:DOCTYPE|ENTITY)\b", text, re.I), "svg-declaration")
    declaration = re.match(r"\A<\?xml[ \t\r\n]+([^?]*)\?>", text)
    if declaration:
        encodings = re.findall(r"""\bencoding\s*=\s*['"]([^'"]+)['"]""", declaration[1], re.I)
        require(len(encodings) <= 1 and all(value.lower() == "utf-8" for value in encodings), "svg-encoding")
    remainder = text[declaration.end():] if declaration else text
    require("<?" not in remainder, "svg-unsafe")
    try:
        root = ET.fromstring(text)
    except ET.ParseError:
        raise AssetError("svg-xml") from None
    require(root.tag == "{" + SVG_NS + "}svg", "svg-root")
    forbidden = {"script", "foreignobject", "animate", "animatemotion", "animatetransform", "set", "discard"}
    for node in root.iter():
        require(node.tag.startswith("{" + SVG_NS + "}"), "svg-unsafe")
        name = node.tag.rsplit("}", 1)[-1].lower()
        require(name not in forbidden, "svg-unsafe")
        if name == "style":
            safe_css("".join(node.itertext()))
        for key, value in node.attrib.items():
            local = key.rsplit("}", 1)[-1].lower()
            require(not local.startswith("on") and local not in {"src", "data", "base"}, "svg-unsafe")
            if local == "href":
                safe_href(value)
            elif local == "style" or re.search(r"url\s*\(", value, re.I):
                safe_css(value)


def verify_assets(root, assets):
    require(type(assets) is list and 0 < len(assets) <= 59, "manifest-shape")
    paths = [asset_relative(entry) for entry in assets]
    require(len({value.casefold() for value in paths}) == len(paths), "asset-duplicate")
    require(sum(entry["bytes"] for entry in assets) <= MAX_TOTAL_BYTES, "asset-total-size")
    total = svg_count = 0
    for entry, relative in zip(assets, paths):
        raw = read_asset(root, relative)
        require(len(raw) == entry["bytes"], "asset-size")
        require(hashlib.sha256(raw).hexdigest() == entry["sha256"], "asset-sha256")
        git_blob = hashlib.sha1(b"blob " + str(len(raw)).encode("ascii") + b"\0" + raw).hexdigest()
        require(git_blob == entry["sourceBlob"], "asset-git-blob")
        if relative.endswith(".svg"):
            verify_svg(raw)
            svg_count += 1
        total += len(raw)
    return {"count": len(assets), "bytes": total, "svgCount": svg_count}


def verify_release(root, raw=None):
    if raw is None:
        raw = read_asset(root, MANIFEST, 128 * 1024)
    require(hashlib.sha256(raw).hexdigest() == MANIFEST_SHA256, "manifest-hash")
    return verify_assets(root, validate_manifest(parse_manifest(raw)))


def main():
    root = Path(__file__).resolve().parents[1]
    try:
        result = verify_release(root)
    except AssetError as error:
        print(json.dumps({"status": "rejected", "category": str(error)}, sort_keys=True))
        return 1
    print(json.dumps({"status": "custody-verified", **result,
                      "publicClaimsAccepted": False, "fullSourceShaAccepted": False}, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
