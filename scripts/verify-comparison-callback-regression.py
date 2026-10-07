"""Hosted negative control against an exact pre-fix service, then rebuild HEAD."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

OLD_COMMIT = "da3651eed32f80a82d030d0413dd4f1717d3e853"
SERVICE_PATH = "Legacy.Maliev.Web.Application/InstantQuotationAuthoritativePricingService.cs"
OLD_SHA256 = "7ebb93ac1e4e4353678d38310f82ee30a44bae48cda232245fe448bbbc7da0f7"
METHODS = (
    "ComparisonCallbackBudget_ClosesAttemptedCardAndPreservesSelectedQuote",
    "ComparisonCallbackAbsoluteDeadline_RejectsQuoteAndLateCompletionCannotResume",
)
TEST_CLASS = "Legacy.Maliev.Web.Tests.InstantQuotationMaterialPricingProgressTests"
EXPECTED_CASES = {
    f"{TEST_CLASS}.{method}(gatedStatus: {status})": method
    for method in METHODS for status in ("Pending", "Completed")
}


def negative_filter() -> str:
    return "|".join(f"FullyQualifiedName={TEST_CLASS}.{method}" for method in METHODS)


def verify_negative_trx(path: Path) -> dict:
    root = ET.parse(path).getroot()
    counters = next(node for node in root.iter() if node.tag.endswith("}Counters"))
    expected = {"total": 4, "executed": 4, "failed": 4, "passed": 0, "notExecuted": 0}
    if any(int(counters.attrib.get(key, "-1")) != value for key, value in expected.items()):
        raise RuntimeError("Pre-fix control must contain exactly four executed assertion failures")
    if any(int(value) != 0 for key, value in counters.attrib.items() if key not in expected):
        raise RuntimeError("Unexpected pre-fix counter category")
    rows = [node for node in root.iter() if node.tag.endswith("}UnitTestResult")]
    if len(rows) != 4:
        raise RuntimeError("Unexpected pre-fix result corpus")
    if len({row.attrib.get("testName") for row in rows}) != 4:
        raise RuntimeError("Duplicate pre-fix control row")
    if {row.attrib.get("testName") for row in rows} != set(EXPECTED_CASES):
        raise RuntimeError("Missing or unexpected qualified Pending/Completed control case")
    seen = {method: 0 for method in METHODS}
    for row in rows:
        method = EXPECTED_CASES.get(row.attrib.get("testName", ""))
        if method is None or row.attrib.get("outcome") != "Failed":
            raise RuntimeError("Unrelated or non-failing pre-fix control result")
        message = next((node.text or "" for node in row.iter() if node.tag.endswith("}Message")), "")
        if not ("Assert.NotNull() Failure" in message or "Assert.False() Failure" in message):
            raise RuntimeError("Pre-fix control failed outside the expected quote/cancellation assertions")
        if "AbsoluteDeadline" in method and "Assert.False() Failure" not in message:
            raise RuntimeError("Absolute control must reject premature callback cancellation")
        seen[method] += 1
    if any(count != 2 for count in seen.values()):
        raise RuntimeError("Missing Pending/Completed control rows")
    return {"expectedFailures": 4, "methods": seen, "trxSha256": hashlib.sha256(path.read_bytes()).hexdigest()}


def run(root: Path, directory: Path, name: str, command: list[str], build: bool = False) -> int:
    # Match the existing acceptance-host build's checked-out dependency selection.
    # The helper itself still requires the authenticated, owned hosted environment.
    environment = dict(os.environ, GITHUB_ACTIONS="false") if build else None
    completed = subprocess.run(command, cwd=root, env=environment,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               text=True, timeout=300, check=False)
    (directory / f"{name}.log").write_text(completed.stdout, encoding="utf-8")
    warnings = re.findall(r"\b(\d+) Warning\(s\)", completed.stdout)
    errors = re.findall(r"\b(\d+) Error\(s\)", completed.stdout)
    if build and (completed.returncode != 0 or not warnings or not errors
                  or any(value != "0" for value in warnings + errors)):
        raise RuntimeError(f"{name} did not build with zero warnings/errors; see preserved log")
    return completed.returncode


def main() -> None:
    root = Path(os.environ.get("GITHUB_WORKSPACE", "")).resolve()
    if (os.environ.get("GITHUB_ACTIONS") != "true"
            or os.environ.get("RUNNER_ENVIRONMENT") != "github-hosted"
            or os.environ.get("GITHUB_REPOSITORY") != "MALIEV-Co-Ltd/Legacy.Maliev.Web"
            or Path.cwd().resolve() != root):
        raise RuntimeError("Pricing negative control requires the owned hosted Web workspace")
    service = root / SERVICE_PATH
    patched = service.read_bytes()
    parent = subprocess.check_output([
        "gh", "api", f"repos/MALIEV-Co-Ltd/Legacy.Maliev.Web/contents/{SERVICE_PATH}?ref={OLD_COMMIT}",
        "-H", "Accept: application/vnd.github.raw+json",
    ], cwd=root, timeout=60)
    if hashlib.sha256(parent).hexdigest() != OLD_SHA256 or parent == patched:
        raise RuntimeError("Pre-fix source hash or distinct patched source check failed")
    directory = root / "pricing-regression-parent-control"
    directory.mkdir()
    project = "Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj"
    build_command = ["dotnet", "build", project, "--configuration", "Release", "--no-restore", "-p:CI=false"]
    try:
        service.write_bytes(parent)
        run(root, directory, "parent-build", build_command, build=True)
        test_exit = run(root, directory, "parent-test", [
            "dotnet", "test", project, "--configuration", "Release", "--no-build", "--no-restore",
            "-p:CI=false", "-p:VSTestCollect=", "-p:RunSettingsFilePath=", "--filter",
            negative_filter(), "--logger", "trx",
            "--results-directory", str(directory / "trx"),
        ])
        if test_exit == 0:
            raise RuntimeError("Regression did not reject the exact pre-fix source")
        files = list((directory / "trx").glob("*.trx"))
        if len(files) != 1:
            raise RuntimeError("Expected one pre-fix TRX")
        result = verify_negative_trx(files[0])
        result.update({"parentCommit": OLD_COMMIT, "parentSourceSha256": OLD_SHA256,
                       "patchedSourceSha256": hashlib.sha256(patched).hexdigest(),
                       "parentBuildWarnings": 0, "parentBuildErrors": 0,
                       "controlPassed": True, "productionAcceptance": False})
        (directory / "receipt.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    finally:
        service.write_bytes(patched)
        if service.read_bytes() != patched:
            raise RuntimeError("Patched source restoration failed")
        run(root, directory, "restored-head-build", build_command, build=True)
        subprocess.run(["git", "diff", "--exit-code", "--", SERVICE_PATH], cwd=root, check=True)
    print("Exact pre-fix source rejected by four assertion rows; patched HEAD restored and rebuilt with zero warnings/errors")


if __name__ == "__main__":
    main()
