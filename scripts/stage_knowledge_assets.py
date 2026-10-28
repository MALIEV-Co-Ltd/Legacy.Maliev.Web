"""Stage exact knowledge source objects offline; never modify a source/target checkout."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat

SEED_BLOB = "5fd485eaecf9ef343e588ce2b737088d8bddd778"
HELPER_BLOB = "e1ae5e1f4c7f33e26d585a2d0f885f46e94958e6"
SOURCE_COMMIT = "770cb58032969ae4a0fe169686b53ab61a44fff8"
SOURCE_TREE = "a1ef9061734c2a57fd6b621b56559c3efa59168e"
MAX_TOTAL = 16 * 1024 * 1024
MAX_OBJECT = 4 * 1024 * 1024


class IntakeError(RuntimeError):
    """Fixed non-sensitive rejection categories."""


def require(value, category):
    if not value:
        raise IntakeError(category)


def git_hash(kind, raw):
    return hashlib.sha1(kind.encode("ascii") + b" " + str(len(raw)).encode("ascii") + b"\0" + raw).hexdigest()


def safe_relative(value):
    require(type(value) is str and value and "\\" not in value and ":" not in value
            and not value.startswith("/") and all(part not in ("", ".", "..") for part in value.split("/")),
            "relative-path")
    return value


def parse_tree(raw):
    entries = {}
    offset = 0
    while offset < len(raw):
        space, zero = raw.find(b" ", offset), raw.find(b"\0", offset)
        require(offset < space < zero and zero + 21 <= len(raw), "tree-object")
        mode = raw[offset:space]
        try:
            name = raw[space + 1:zero].decode("utf-8", "strict")
        except UnicodeError:
            raise IntakeError("tree-name") from None
        require(name not in entries and "/" not in name and name not in ("", ".", ".."), "tree-name")
        entries[name] = (mode, raw[zero + 1:zero + 21].hex())
        offset = zero + 21
    return entries


class SourceObjects:
    def __init__(self, root, reader):
        self.root, self.reader, self.trees, self.total = root, reader, {}, 0

    def read(self, kind, sha):
        require(re.fullmatch(r"[0-9a-f]{40}", sha) is not None, "object-id")
        raw = self.reader(self.root, ".source-objects/" + sha, MAX_OBJECT)
        self.total += len(raw)
        require(self.total <= MAX_TOTAL and git_hash(kind, raw) == sha, "object-custody")
        return raw

    def bind_commit(self):
        raw = self.read("commit", SOURCE_COMMIT)
        require(raw.split(b"\n", 1)[0] == b"tree " + SOURCE_TREE.encode("ascii"), "commit-tree")

    def bind_path(self, path, expected):
        tree = SOURCE_TREE
        parts = safe_relative(path).split("/")
        for index, part in enumerate(parts):
            if tree not in self.trees:
                require(len(self.trees) < 128, "tree-limit")
                self.trees[tree] = parse_tree(self.read("tree", tree))
            require(part in self.trees[tree], "tree-path")
            mode, child = self.trees[tree][part]
            if index == len(parts) - 1:
                require(mode == b"100644" and child == expected, "tree-blob")
            else:
                require(mode in (b"40000", b"040000"), "tree-directory")
                tree = child


def bounded_read(root, relative, maximum):
    root = Path(root)
    require(root.is_absolute() and root.is_dir() and not root.is_symlink(), "read-root")
    current = root
    for part in safe_relative(relative).split("/"):
        current = current / part
        info = current.lstat()
        require(not stat.S_ISLNK(info.st_mode) and not getattr(info, "st_file_attributes", 0) & 1024,
                "read-link")
    require(stat.S_ISREG(info.st_mode) and 0 < info.st_size <= maximum, "read-size")
    descriptor = os.open(current, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    with os.fdopen(descriptor, "rb") as stream:
        opened = os.fstat(stream.fileno())
        identity = lambda value: (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns)
        require(identity(opened) == identity(info), "read-race")
        raw = stream.read(maximum + 1)
        after = os.fstat(stream.fileno())
    require(len(raw) == info.st_size and identity(after) == identity(info)
            and identity(current.lstat()) == identity(info), "read-race")
    return raw


def load_policy(target):
    # Load only the reviewed, byte-bound helper; do not import mutable arbitrary code.
    path = target / "scripts/verify_company_release_assets.py"
    raw = bounded_read(target, "scripts/verify_company_release_assets.py", 64 * 1024)
    require(git_hash("blob", raw) == HELPER_BLOB, "policy-custody")
    namespace = {"__name__": "knowledge_asset_policy", "__file__": str(path)}
    exec(compile(raw, str(path), "exec"), namespace)
    return namespace


def validate_seed(seed):
    require(type(seed) is dict and seed.get("schema") == "maliev-knowledge-source-intake.v1"
            and seed.get("sourceCommit") == SOURCE_COMMIT and seed.get("sourceTree") == SOURCE_TREE,
            "seed-shape")
    rows, inherited = seed["rows"], seed["inherited"]
    require(len(rows) == 70 and len(inherited) == 2, "seed-count")
    require([sum(row["kind"] == kind for row in rows) for kind in ("webp", "svg", "provenance")]
            == [42, 17, 11], "seed-kinds")
    for key in ("sourcePath", "targetPath"):
        paths = [safe_relative(row[key]) for row in rows + inherited]
        require(len({path.casefold() for path in paths}) == 72, "seed-paths")
    photos = {row["sourcePath"] for row in rows if row["kind"] == "webp"}
    require(len({path.removesuffix("-small.webp") for path in photos if path.endswith("-small.webp")}) == 21
            and all(path.removesuffix("-small.webp") + ".webp" in photos
                    for path in photos if path.endswith("-small.webp")), "photo-pairs")
    require(sum(row["bytes"] for row in rows + inherited) <= MAX_TOTAL, "source-total")
    return rows, inherited


def verify_blob(raw, row):
    require(len(raw) == row["bytes"] and git_hash("blob", raw) == row["blob"], "source-blob")


def collect(source, target, seed, policy):
    rows, inherited = validate_seed(seed)
    read = policy["read_asset"]
    objects = SourceObjects(source, read)
    objects.bind_commit()
    # The existing company custody guard remains mandatory and unchanged.
    prior = policy["verify_release"](target)
    require(prior["count"] == 59, "prior-company-custody")
    payloads, measured = {}, []
    for row in rows + inherited:
        objects.bind_path(row["sourcePath"], row["blob"])
        raw = read(source, row["sourcePath"])
        verify_blob(raw, row)
        if row["sourcePath"].endswith(".svg"):
            policy["verify_svg"](raw)
        target_path = target / row["targetPath"]
        if row in inherited:
            verify_blob(read(target, row["targetPath"]), row)
            continue
        preimage = row["targetPreimage"]
        if preimage is None:
            require(not os.path.lexists(target_path), "target-collision")
        else:
            require(git_hash("blob", read(target, row["targetPath"])) == preimage, "target-preimage")
        payloads[row["targetPath"]] = raw
        measured.append({**row, "sha256": hashlib.sha256(raw).hexdigest()})
    return payloads, measured


def distinct_roots(source, target, output):
    source, target, output = (Path(value).absolute() for value in (source, target, output))
    require(source.is_dir() and target.is_dir() and not source.is_symlink() and not target.is_symlink(),
            "root-path")
    require(output.parent.is_dir() and not output.parent.is_symlink()
            and output.parent.resolve() == output.parent and not os.path.lexists(output), "output-path")
    roots = [source.resolve(), target.resolve(), output]
    require(all(not left.is_relative_to(right) and not right.is_relative_to(left)
                for index, left in enumerate(roots) for right in roots[index + 1:]), "root-overlap")
    return source, target, output


def write_new(root, relative, raw):
    path = root / safe_relative(relative)
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0), 0o600)
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(raw)


def stage(source, target, output, seed_raw):
    source, target, output = distinct_roots(source, target, output)
    require(git_hash("blob", seed_raw) == SEED_BLOB, "seed-custody")
    policy = load_policy(target)
    seed = policy["parse_manifest"](seed_raw)
    payloads, measured = collect(source, target, seed, policy)
    manifest = {"schema": "maliev-knowledge-assets.v1", "sourceCommit": SOURCE_COMMIT,
                "sourceTree": SOURCE_TREE, "seedBlob": SEED_BLOB, "rows": measured,
                "inherited": seed["inherited"], "decodedDimensionsVerified": False,
                "publicHttpVerified": False, "rightsCertified": False, "releaseAccepted": False}
    raw = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    # Exclusive new, disjoint output only. Any partial output is retained as incomplete evidence.
    output.mkdir(mode=0o700)
    for path, content in payloads.items():
        write_new(output, path, content)
    write_new(output, "docs/migration/knowledge-assets.v1.json", raw)
    write_new(output, "docs/migration/knowledge-source-intake.v1.json", seed_raw)
    write_new(output, "STAGING_COMPLETE.json", (json.dumps({
        "schema": 1, "files": len(payloads), "manifestSha256": hashlib.sha256(raw).hexdigest(),
        "sourceCommit": SOURCE_COMMIT, "releaseAccepted": False}, sort_keys=True) + "\n").encode("ascii"))
    return {"status": "staged-source-only", "files": 70,
            "manifestSha256": hashlib.sha256(raw).hexdigest(), "releaseAccepted": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-export", required=True)
    parser.add_argument("--target-root", required=True)
    parser.add_argument("--new-output", required=True)
    parser.add_argument("--seed", required=True)
    args = parser.parse_args()
    # Seed is small and byte-bound before any import or output operation.
    with open(args.seed, "rb") as stream:
        seed = stream.read(128 * 1024 + 1)
    require(len(seed) <= 128 * 1024, "seed-size")
    print(json.dumps(stage(args.source_export, args.target_root, args.new_output, seed), sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
