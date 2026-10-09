"""Fail-closed focused Web TRX qualification; no application/runtime mutation."""
import argparse
from collections import Counter
from pathlib import Path
import re
import uuid
import xml.etree.ElementTree as ET

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
FIELDS = set("total executed passed failed error timeout aborted inconclusive passedButRunAborted notRunnable notExecuted disconnected warning completed inProgress pending".split())
# Frozen source-approved class/method row counts; never inferred from the candidate TRX.
ROSTERS = {'fdm': {'Legacy.Maliev.Web.Tests.FdmOriginalServiceEligibilityBoundaryTests': {'OriginalEligibleMaterialBuild_RejectsStaleAndForeignBindingsBeforeReadingBytes': 2,
                                                                                'OriginalEligibleMaterialBuild_RequiresAdmittedUploadAndProducesBoundPhysicalPrice': 2},
         'Legacy.Maliev.Web.Tests.FdmSimulationModelPathsTests': {'Build_Bridges_RequireTwoSupportedAnchors': 1,
                                                                  'Build_Gyroid_MatchesDensityAndIsRepeatableAcrossPhase': 1,
                                                                  'Build_HollowPart_NeverDrawsSparseInfillThroughHole': 1,
                                                                  'Build_LinearInfill_MatchesRequestedFraction': 2,
                                                                  'Build_OverhangWall_SplitsSpeedWithoutDuplicatingRole': 1,
                                                                  'Build_SteppedPart_PropagatesTopSkinBelowLedge': 1,
                                                                  'Build_ThinSlab_DoesNotDoubleCountTopAndBottomSkin': 1,
                                                                  'Build_ThinWall_DoesNotInventSixFullLoops': 1,
                                                                  'Build_Walls_KeepOuterAndInnerRolesDistinct': 1},
         'Legacy.Maliev.Web.Tests.FdmSimulationProfileTests': {'ResolveBuildProfile_IncompatibleNozzle_IsRejected': 1,
                                                               'ResolveBuildProfile_ResolvedHashIncludesInheritedSpeed': 1,
                                                               'ResolveBuildProfile_UsesResolvedLayerHeightAndWalls': 3,
                                                               'ResolveBuildProfile_ZeroVolumetricFlow_IsUnlimited': 1,
                                                               'RuntimeProvider_CoversAllMaterialsAndBuildsWithExplicitEligibility': 1,
                                                               'RuntimeProvider_UsesApprovedCommercialFilamentProfiles': 5,
                                                               'RuntimeProvider_UsesCurrentEsunTpuDensityFlowAndMachinePreparationSequence': 1,
                                                               'RuntimeProvider_UsesFilamentSpecificMinimumLayerTime': 8,
                                                               'RuntimeProvider_UsesPackagedBambuProcessSnapshots': 3,
                                                               'SimulationResultSchema_DeclaresDisjointPhysicalVolumes': 1,
                                                               'SimulationResult_SupportAndTotalVolumesReconcile': 1},
         'Legacy.Maliev.Web.Tests.FdmSimulationScenarioTests': {'Evaluate_BrimExceedsPlate_RequiresMachineOrPartition': 1,
                                                                'Evaluate_ExcludedZone_PreventsFalseFit': 1,
                                                                'Evaluate_OversizePart_ProducesReviewedPartition': 2,
                                                                'Evaluate_PartFitsOnlyDiagonally_SelectsFeasiblePose': 1,
                                                                'Evaluate_Quantity_PacksAcrossExpectedPlateCount': 3,
                                                                'GenerateCandidates_IsDeterministicAndHonorsFixedPose': 1},
         'Legacy.Maliev.Web.Tests.FdmSimulationSupportTests': {'BuildTree_AngleLimit_IsReported': 1,
                                                               'BuildTree_HollowTrunk_DepositsLessThanSolidTrunk': 1,
                                                               'BuildTree_ModelCollision_ReportsUnreachableTip': 1,
                                                               'BuildTree_NearbyTips_MergeIntoSharedTrunk': 1,
                                                               'Build_Cantilever_ProducesAuditableSupport': 1,
                                                               'Build_InternalCeiling_ReportsAccessibilityFailure': 1,
                                                               'Build_OverlappingRoofs_UnionsSharedTower': 1,
                                                               'Build_SupportOff_ReportsDemandInsteadOfZeroSuccess': 1,
                                                               'Build_VerticalCube_ProducesNoSupport': 1,
                                                               'CalculateMass_UsesDepositedVolumesAndSeparateDensities': 1}},
 'sidebar': {'Legacy.Maliev.Web.Tests.InstantQuotationSidebarLayoutBrowserTests': {'RealPartsRailExposesLongNamesAndKeyboardActions': 10}}}


def verify(path, suite, assembly):
    root = ET.parse(path).getroot()
    def one(parent, name):
        rows = parent.findall(NS + name)
        if len(rows) != 1:
            raise ValueError("Exactly one TRX " + name + " required")
        return rows[0]
    if root.tag != NS + "TestRun":
        raise ValueError("Exact native TRX namespace required")
    summary = one(root, "ResultSummary")
    counters = one(summary, "Counters")
    if set(counters.attrib) != FIELDS or any(not re.fullmatch(r"0|[1-9][0-9]*", v) for v in counters.attrib.values()):
        raise ValueError("All 16 canonical TRX counters required")
    counts = {k: int(v) for k, v in counters.attrib.items()}
    expected = ROSTERS[suite]
    total = sum(sum(methods.values()) for methods in expected.values())
    if (summary.attrib.get("outcome") != "Completed"
        or any(counts[k] != total for k in ("total", "executed", "passed"))
        or any(counts[k] for k in FIELDS - {"total", "executed", "passed"})):
        raise ValueError("Exact all-passed focused counters required; no skips or incomplete states")
    groups = []
    for name, child in (("Results", "UnitTestResult"), ("TestDefinitions", "UnitTest"), ("TestEntries", "TestEntry")):
        container = one(root, name)
        if len(container) != total or any(row.tag != NS + child for row in container):
            raise ValueError("Exact result/definition/entry rows required")
        groups.append(list(container))
    results, definitions, entries = groups
    def ids(rows, key):
        values = [row.attrib.get(key, "") for row in rows]
        if len(set(values)) != total or any(str(uuid.UUID(v)) != v for v in values):
            raise ValueError("Missing, duplicate or noncanonical " + key)
        return set(values)
    if ids(results, "testId") != ids(definitions, "id") or ids(entries, "testId") != ids(results, "testId"):
        raise ValueError("Definition/result/entry test identity mismatch")
    if ids(results, "executionId") != ids(entries, "executionId"):
        raise ValueError("Execution identity mismatch")
    if {(r.attrib["testId"], r.attrib["executionId"]) for r in results} != {(r.attrib["testId"], r.attrib["executionId"]) for r in entries}:
        raise ValueError("Execution entry association mismatch")
    definitions = {row.attrib["id"]: row for row in definitions}
    names = set()
    actual = Counter()
    assembly = str(Path(assembly).resolve())
    for result in results:
        definition = definitions[result.attrib["testId"]]
        method = one(definition, "TestMethod")
        execution = one(definition, "Execution")
        cls, member = method.attrib.get("className"), method.attrib.get("name")
        name = result.attrib.get("testName", "")
        if (result.attrib.get("outcome") != "Passed" or name in names
            or definition.attrib.get("name") != name
            or execution.attrib.get("id") != result.attrib["executionId"]
            or cls not in expected or member not in expected[cls]
            or not (name == cls + "." + member or name.startswith(cls + "." + member + "("))
            or str(Path(method.attrib.get("codeBase", "")).resolve()) != assembly
            # Native VSTest emits lowercased storage on Linux; codeBase remains exact case.
            or str(Path(definition.attrib.get("storage", "")).resolve()).casefold() != assembly.casefold()):
            raise ValueError("Unknown, duplicate, failed or mismatched test/definition/execution/assembly")
        names.add(name)
        actual[(cls, member)] += 1
    if actual != Counter({(cls, member): rows for cls, methods in expected.items() for member, rows in methods.items()}):
        raise ValueError("Exact independent class/method row roster required")
    if suite == "sidebar":
        cls = "Legacy.Maliev.Web.Tests.InstantQuotationSidebarLayoutBrowserTests"
        exact = {f'{cls}.RealPartsRailExposesLongNamesAndKeyboardActions(culture: "{culture}", width: {width})'
                 for culture in ("en", "th") for width in (320, 375, 820, 992, 1280)}
        if names != exact:
            raise ValueError("Exact ten sidebar culture/viewport rows required")
    return counts


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--suite", required=True, choices=ROSTERS)
    parser.add_argument("--assembly", required=True, type=Path)
    args = parser.parse_args()
    paths = list(args.directory.rglob("*.trx"))
    if len(paths) != 1 or not args.assembly.is_file():
        raise ValueError("Exactly one focused TRX and actual built assembly required")
    counts = verify(paths[0], args.suite, args.assembly)
    print(f"Focused {args.suite}: {counts['executed']} executed / {counts['passed']} passed; exact identities and all counters clean")


if __name__ == "__main__":
    main()
