"""Keep hosted Noble security updates on the signed official HTTPS security pocket."""
import os
from pathlib import Path
import re
import stat
import sys
import tempfile

MIRROR = "mirror+file:/etc/apt/apt-mirrors.txt"
SECURITY = "https://security.ubuntu.com/ubuntu/"
KEYRING = "/usr/share/keyrings/ubuntu-archive-keyring.gpg"
REQUIRED = {"Types", "URIs", "Suites", "Components", "Signed-By"}
MAIN = {"noble", "noble-updates", "noble-backports"}


def configure(source):
    sections = re.split(r"\n[ \t]*\n", source.replace("\r\n", "\n").strip())
    blocks = [section for section in sections
              if any(line.strip() and not line.lstrip().startswith("#") for line in section.splitlines())]
    if len(blocks) not in (1, 2):
        raise ValueError("Unexpected hosted Ubuntu stanza count")
    parsed = []
    for block in blocks:
        fields = {}
        for line in block.splitlines():
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            match = re.fullmatch(r"([A-Za-z-]+):[ \t]+(.+)", line)
            if not match or match[1] in fields:
                raise ValueError("Unexpected hosted Ubuntu field")
            fields[match[1]] = match[2].strip()
        if set(fields) != REQUIRED or fields["Types"] != "deb":
            raise ValueError("Unexpected hosted Ubuntu source policy")
        if fields["URIs"] != MIRROR or fields["Signed-By"] != KEYRING:
            raise ValueError("Unexpected hosted Ubuntu trust or URI policy")
        components = fields["Components"].split()
        if len(components) != 4 or set(components) != {"main", "restricted", "universe", "multiverse"}:
            raise ValueError("Unexpected hosted Ubuntu components")
        suites = fields["Suites"].split()
        if len(suites) != len(set(suites)):
            raise ValueError("Duplicate hosted Ubuntu suites")
        parsed.append((block, fields, suites))
    if len(parsed) == 1:
        if set(parsed[0][2]) != MAIN | {"noble-security"}:
            raise ValueError("Unexpected combined hosted Ubuntu suites")
    elif sorted((frozenset(item[2]) for item in parsed), key=lambda item: len(item)) != [frozenset({"noble-security"}), frozenset(MAIN)]:
        raise ValueError("Unexpected mixed hosted Ubuntu suites")

    def replace(block, name, value):
        return re.sub(r"(?m)^" + re.escape(name) + r":[ \t]+.*$", name + ": " + value, block)

    result = []
    stanzas = iter(parsed)
    for section in sections:
        if not any(line.strip() and not line.lstrip().startswith("#") for line in section.splitlines()):
            result.append(section)
            continue
        block, fields, suites = next(stanzas)
        if "noble-security" not in suites:
            result.append(block)
            continue
        remaining = [suite for suite in suites if suite != "noble-security"]
        if remaining:
            result.append(replace(block, "Suites", " ".join(remaining)))
        security = replace(block, "Suites", "noble-security")
        result.append(replace(security, "URIs", SECURITY))
    return "\n\n".join(result) + "\n"


def main():
    if sys.platform != "linux":
        raise ValueError("Hosted Ubuntu source configuration requires Linux")
    source = Path("/etc/apt/sources.list.d/ubuntu.sources")
    if source.is_symlink() or not stat.S_ISREG(source.stat().st_mode):
        raise ValueError("Unexpected hosted Ubuntu source file")
    original = source.stat()
    configured = configure(source.read_text())
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", newline="\n",
                                         prefix=".maliev-web-browser-", dir=source.parent, delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(configured)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temporary, stat.S_IMODE(original.st_mode))
        os.chown(temporary, original.st_uid, original.st_gid)
        if source.is_symlink() or source.stat().st_ino != original.st_ino:
            raise ValueError("Hosted Ubuntu source file changed during validation")
        os.replace(temporary, source)
        temporary = None
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
    print("Hosted Noble security pocket uses official HTTPS; suites/components/signing key preserved.")


if __name__ == "__main__":
    main()
