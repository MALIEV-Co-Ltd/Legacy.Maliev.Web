"""Qualify the frozen Career consumer and original-oracle execution identities."""
import argparse
from collections import Counter
import hashlib
import importlib.util
import json
from pathlib import Path
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
MANIFEST_SHA256 = "411b41a0de2c04a4e1cc03a54903a7f98c5685348665ff6ab1148cd444908274"
spec = importlib.util.spec_from_file_location("career_shared_trx_gate", HERE / "verify-focused-web-trx.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


def roster(suite):
    raw = (HERE / "career-focused-identities.json").read_bytes()
    if hashlib.sha256(raw).hexdigest() != MANIFEST_SHA256:
        raise ValueError("Reviewed Career identity manifest changed")
    manifest = json.loads(raw)
    expected = manifest["suites"][suite]
    if len(expected) != {"consumer": 240, "original": 4}[suite] or len(set(expected)) != len(expected):
        raise ValueError("Exact independent Career identity inventory required")
    return expected


def verify(path, suite, assembly):
    expected = roster(suite)
    counts = Counter(tuple(name.split("(", 1)[0].rsplit(".", 1)) for name in expected)
    methods = {}
    for (cls, method), count in counts.items():
        methods.setdefault(cls, {})[method] = count
    gate.ROSTERS["career-" + suite] = methods
    result = gate.verify(path, "career-" + suite, assembly)
    actual = {row.attrib["testName"] for row in ET.parse(path).getroot().findall(gate.NS + "Results/" + gate.NS + "UnitTestResult")}
    if actual != set(expected):
        raise ValueError("Exact Career parameterized execution identities required")
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--suite", required=True, choices=("consumer", "original"))
    parser.add_argument("--assembly", required=True, type=Path)
    args = parser.parse_args()
    paths = list(args.directory.rglob("*.trx"))
    if len(paths) != 1 or not args.assembly.is_file():
        raise ValueError("Exactly one Career TRX and actual built assembly required")
    result = verify(paths[0], args.suite, args.assembly)
    print(f"Career {args.suite}: {result['passed']} exact identities passed; native counters and joins verified")


if __name__ == "__main__":
    main()
