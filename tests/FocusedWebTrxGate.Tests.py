"""Parser controls only; synthetic XML and marker files are not native test evidence."""
import copy
import contextlib
import io
import textwrap
import importlib.util
from pathlib import Path
import tempfile
import unittest
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("focused_gate", ROOT / "scripts/verify-focused-web-trx.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
FIELDS = "total executed passed failed error timeout aborted inconclusive passedButRunAborted notRunnable notExecuted disconnected warning completed inProgress pending".split()

# Retained147 family/count inventory frozen from b3, independent of the live gate.
MIGRATION_CONSUMERS_RETAINED_147 = {
    "Legacy.Maliev.Web.Tests.PublicCrawlerInventoryAcceptanceTests": {
        "SourceInventory_ActualEnglishAndThaiDocumentsKeepTheirDiscoveryContract": 54,
        "RetainedThreePublicLayouts_KeepTheSameHttpDiscoveryContract": 6,
        "SourceCommercialPages_RenderIndependentIntentAndDirectQuotationAnchors": 20,
        "AdditiveNoWeaponsRoute_DoesNotReplaceAnyHistoricalInventoryDocument": 2,
        "PublicInventoryAcceptance_DoesNotRelaxPrivateAndCredentialUtilities": 4,
    },
    "Legacy.Maliev.Web.Tests.SitemapResponseCachingHttpTests": {
        "Public_sitemap_declares_source_one_hour_public_cache_lifetime": 2,
        "Public_sitemap_keeps_exact_canonical_inventory_localized_alternates_and_private_exclusions": 2,
        "Public_sitemap_xml_is_identical_across_cultures_and_caller_cookie_input": 1,
    },
    "Legacy.Maliev.Web.Tests.KnowledgeGuidelinesStaticSsrRouteTests": {
        "Host_DeclaresTheGuidelinesStaticSsrPageAndKeepsTheRazorRollbackSource": 1,
        "GuidelinesRoute_RendersCompleteLocalizedAccessibleStaticDocument": 2,
        "GuidelinesRoute_PreservesCanonicalAndLocalizedAlternates": 2,
        "GuidelinesRoute_EmitsLocalizedValidHowToStructuredData": 2,
        "AcceptedConsent_PreservesTheGtmBodyContainerOnTheGuidelinesRoute": 1,
        "DisabledGuidelinesRoute_UsesTheRetainedRazorFallbackAtTheCanonicalUrl": 1,
    },
    "Legacy.Maliev.Web.Tests.KnowledgeCncMachiningStaticSsrRouteTests": {
        "Host_DeclaresTheCncMachiningSpecificationPageAndKeepsTheRazorRollbackSource": 1,
        "CncMachiningSpecificationRoute_RendersCompleteLocalizedAccessibleDocument": 2,
        "SourceFinalSurfaceFinishesRoute_PreservesTheRelocatedAnodizingDetail": 2,
        "CncMachiningSpecificationRoute_PreservesCanonicalAndLocalizedAlternates": 2,
        "CncMachiningSpecificationRoute_EmitsLocalizedWebPageAndFourLevelBreadcrumbSchema": 2,
        "AcceptedConsent_PreservesTheGtmBodyContainerOnTheCncMachiningSpecificationRoute": 1,
        "DisabledCncMachiningSpecificationRoute_UsesTheRetainedRazorFallback": 1,
    },
    "Legacy.Maliev.Web.Tests.PublicCopyParityTests": {
        "CareerDirectoryKeepsThePostA40ResponsiveAndCloudflareSafeMarkup": 1,
        "LegalDocumentsExposeTheLocalizedEffectiveDateAndCompleteTocAnchors": 1,
        "KnowledgeAndInquirySurfacesKeepLocalizedProjectCopyAndProgressiveEnhancementMarkers": 1,
    },
    "Legacy.Maliev.Web.Tests.AssetDeliveryContractTests": {
        "RazorPartials_ReferenceNegotiatedAssetsInsteadOfGzipFiles": 1,
        "GeneratedProductionAsset_IsPresentAndNonEmpty": 4,
        "CncQuotationRuntimeAsset_IsPresentAndNonEmpty": 11,
        "ObsoleteAnimationDependencies_AreAbsentFromSourceAndBundles": 1,
        "ObsoleteJqueryValidationStack_IsAbsentFromSourceAndBundles": 1,
        "KnowledgeActionLinks_KeepAccessibleButtonTextContrast": 1,
        "InstantQuotationAssets_AreBundledAndKeepPricingOnServerEndpoints": 1,
    },
    "Legacy.Maliev.Web.Tests.PublicWebpImageContractTests": {
        "SourceDerivedWebpAssets_MatchCommit6509356": 1,
        "PublicRuntimeSources_ReferenceOnlyWebpRasterImages": 1,
        "PublicWebpReferences_ResolveToCommittedAssets": 1,
    },
    "Legacy.Maliev.Web.Tests.AboutPageContractTests": {
        "About_RendersLocalizedSourceStoryAndAccessibleControls": 2,
        "About_PreservesOrderedIllustrativeProjectAndEngineeringLimits": 1,
        "About_PreservesExactCustomerRosterAndCorrectedArtwork": 2,
    },
    "Legacy.Maliev.Web.Tests.WebSurfaceTests": {
        "HomePage_RendersLocalizedStaticBlazorBodyWithNavigationParity": 2,
        "AboutRoute_PreservesLocalizedStaticSsrTimelineWithoutFacebookProfileIntegration": 2,
        "Sitemap_PublishesEveryIndexedRouteWithLocalizedAlternates": 1,
    },
}
ABOUT_CTA_CLASS = "Legacy.Maliev.Web.Tests.AboutPageContractTests"
ABOUT_CTA_METHOD = "AboutContent_HidesCustomersAndPreservesFollowingCallToAction"
APPROVED_ABOUT_CTA_NAMES = (
    f'{ABOUT_CTA_CLASS}.{ABOUT_CTA_METHOD}(culture: "en", callToAction: "Have an idea, drawing or sample part?")',
    f'{ABOUT_CTA_CLASS}.{ABOUT_CTA_METHOD}(culture: "th", callToAction: "มีไอเดีย แบบงาน หรือชิ้นงานตัวอย่าง?")',
)

# Frozen CNC source8a6bd8313d14457ebc4ba7d4b3e93872baac3bab display cases.
# Native147TRX reports EN828/TH811 result names and447-unit definitions.
PINNED_CNC_DISPLAY_CASES = [
    ("en", "Legacy.Maliev.Web.Tests.KnowledgeCncMachiningStaticSsrRouteTests.CncMachiningSpecificationRoute_RendersCompleteLocalizedAccessibleDocument(culture: \"en\", title: \"CNC machining specifications | MALIEV\", description: \"A solid 3D model defines nominal geometry; a drawi\"···, heading: \"Design decisions that make CNC parts easier to man\"···, eyebrow: \"CNC machining design guide\", firstStep: \"Files and quotation checklist\", firstStepDescription: \"Supply a solid CAD model such as STEP and a readab\"···, secondStep: \"Thin walls and floors: consider support as well as\"···, secondStepDescription: \"Wall height, thickness, unsupported length and mat\"···, thirdStep: \"Internal radii and pockets: leave room for the cut\"···, thirdStepDescription: \"A rotating end mill leaves a radius in an internal\"···, serviceLink: \"Continue reading\")", "Legacy.Maliev.Web.Tests.KnowledgeCncMachiningStaticSsrRouteTests.CncMachiningSpecificationRoute_RendersCompleteLocalizedAccessibleDocument(culture: \"en\", title: \"CNC machining specifications | MALIEV\", description: \"A solid 3D model defines nominal geometry; a drawi\"···, heading: \"Design decisions that make CNC parts easier to man\"···, eyebrow: \"CNC machining design guide\", firstStep: \"Files and quotation checklist\", firstStepDescription: \"···"),
    ("th", "Legacy.Maliev.Web.Tests.KnowledgeCncMachiningStaticSsrRouteTests.CncMachiningSpecificationRoute_RendersCompleteLocalizedAccessibleDocument(culture: \"th\", title: \"ข้อแนะนำการออกแบบชิ้นงาน CNC | MALIEV\", description: \"โมเดล solid 3 มิติกำหนดรูปทรงและขนาดพิกัด ส่วนแบบง\"···, heading: \"ออกแบบชิ้นงาน CNC ให้ผลิตได้ตรงความต้องการ\", eyebrow: \"คู่มือออกแบบชิ้นงานสำหรับการผลิตด้วย CNC\", firstStep: \"ไฟล์และรายการข้อมูลสำหรับขอราคา\", firstStepDescription: \"ส่งโมเดล solid CAD เช่น STEP พร้อมแบบระบุขนาดที่อ่\"···, secondStep: \"ผนังและพื้นบาง: พิจารณาจุดรองรับร่วมกับความหนา\", secondStepDescription: \"ความสูง ความหนา ความยาวช่วงที่ไม่มีตัวรองรับ และวั\"···, thirdStep: \"รัศมีมุมในและโพรง: เผื่อพื้นที่ให้เครื่องมือตัด\", thirdStepDescription: \"ดอกเอ็นมิลหมุนตัดจะทิ้งรัศมีไว้ที่มุมในแนวตั้ง การ\"···, serviceLink: \"อ่านต่อ\")", "Legacy.Maliev.Web.Tests.KnowledgeCncMachiningStaticSsrRouteTests.CncMachiningSpecificationRoute_RendersCompleteLocalizedAccessibleDocument(culture: \"th\", title: \"ข้อแนะนำการออกแบบชิ้นงาน CNC | MALIEV\", description: \"โมเดล solid 3 มิติกำหนดรูปทรงและขนาดพิกัด ส่วนแบบง\"···, heading: \"ออกแบบชิ้นงาน CNC ให้ผลิตได้ตรงความต้องการ\", eyebrow: \"คู่มือออกแบบชิ้นงานสำหรับการผลิตด้วย CNC\", firstStep: \"ไฟล์และรายการข้อมูลสำหรับขอราคา\", firstStepDescripti···"),
]


class FocusedGateControls(unittest.TestCase):
    def fixture(self, directory, suite, independent_roster=None):
        dll = directory / "Legacy.Maliev.Web.Tests.dll"
        dll.write_bytes(b"Synthetic parser control marker, not a native assembly")
        root = ET.Element(NS + "TestRun")
        summary = ET.SubElement(root, NS + "ResultSummary", outcome="Completed")
        roster = gate.ROSTERS[suite] if independent_roster is None else independent_roster
        total = sum(sum(methods.values()) for methods in roster.values())
        ET.SubElement(summary, NS + "Counters", {k: str(total) if k in ("total", "executed", "passed") else "0" for k in FIELDS})
        results = ET.SubElement(root, NS + "Results")
        definitions = ET.SubElement(root, NS + "TestDefinitions")
        entries = ET.SubElement(root, NS + "TestEntries")
        for cls, methods in roster.items():
            for method, rows in methods.items():
                for index in range(rows):
                    if suite == "sidebar" and method == "RealPartsRailExposesLongNamesAndKeyboardActions":
                        culture, width = [(c, w) for c in ("en", "th") for w in (320, 375, 820, 992, 1024, 1280, 1600)][index]
                        name = f'{cls}.{method}(culture: "{culture}", width: {width})'
                    elif suite == "sidebar" and method == "CompactPartsRailKeepsFullNamesAndKeyboardActionsReachable":
                        culture = ("en", "th")[index]
                        name = f'{cls}.{method}(culture: "{culture}")'
                    elif suite == "billing-lifetime":
                        cases = [name for name in gate.BILLING_LIFETIME_CASES
                                 if name == f"{cls}.{method}" or name.startswith(f"{cls}.{method}(")]
                        name = cases[index]
                    else:
                        name = f"{cls}.{method}(syntheticControl: {index})"
                    test, execution = str(uuid.uuid4()), str(uuid.uuid4())
                    definition = ET.SubElement(definitions, NS + "UnitTest", id=test, name=name, storage=str(dll).lower())
                    ET.SubElement(definition, NS + "Execution", id=execution)
                    ET.SubElement(definition, NS + "TestMethod", className=cls, name=method, codeBase=str(dll))
                    ET.SubElement(results, NS + "UnitTestResult", testId=test, executionId=execution, testName=name, outcome="Passed")
                    ET.SubElement(entries, NS + "TestEntry", testId=test, executionId=execution)
        return root, dll

    def verify(self, root, directory, dll, suite):
        path = directory / "focused.trx"
        ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
        return gate.verify(path, suite, dll)

    def independent_about_fixture(self, directory):
        roster = copy.deepcopy(MIGRATION_CONSUMERS_RETAINED_147)
        roster[ABOUT_CTA_CLASS][ABOUT_CTA_METHOD] = 2
        root, dll = self.fixture(directory, "migration-consumers", roster)
        definitions = {row.get("id"): row for row in root.find(NS + "TestDefinitions")}
        about_rows = []
        for result in root.find(NS + "Results"):
            definition = definitions[result.get("testId")]
            method = definition.find(NS + "TestMethod")
            if method.get("className") == ABOUT_CTA_CLASS and method.get("name") == ABOUT_CTA_METHOD:
                about_rows.append((result, definition))
        self.assertEqual(2, len(about_rows))
        for (result, definition), name in zip(about_rows, APPROVED_ABOUT_CTA_NAMES, strict=True):
            result.set("testName", name)
            definition.set("name", name)
        return root, dll, about_rows

    def test_retained147_roster_and_only_approved_about_pair_are_present(self):
        self.assertEqual(147, sum(sum(methods.values()) for methods in MIGRATION_CONSUMERS_RETAINED_147.values()))
        current = copy.deepcopy(gate.ROSTERS["migration-consumers"])
        self.assertEqual(2, current[ABOUT_CTA_CLASS].pop(ABOUT_CTA_METHOD, None))
        self.assertEqual(MIGRATION_CONSUMERS_RETAINED_147, current)

    def test_independent149_source_fixture_is_accepted(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            root, dll, _ = self.independent_about_fixture(directory)
            self.assertEqual(147 + 2, self.verify(root, directory, dll, "migration-consumers")["passed"])

    def test_old147_without_approved_about_pair_is_not_complete(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            root, dll = self.fixture(directory, "migration-consumers", MIGRATION_CONSUMERS_RETAINED_147)
            with self.assertRaises(ValueError):
                self.verify(root, directory, dll, "migration-consumers")

    def test_each_approved_about_case_rejects_missing_extra_unknown_duplicate_or_nonpassed(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            for case in range(2):
                for mutation in ("missing", "extra", "unknown", "duplicate-sourcecase", "failed", "skipped"):
                    with self.subTest(case=case, mutation=mutation):
                        root, dll, about_rows = self.independent_about_fixture(directory)
                        result, definition = about_rows[case]
                        results = root.find(NS + "Results")
                        definitions = root.find(NS + "TestDefinitions")
                        entries = root.find(NS + "TestEntries")
                        entry = next(row for row in entries if row.get("testId") == result.get("testId"))
                        counters = root.find(NS + "ResultSummary/" + NS + "Counters")
                        if mutation == "missing":
                            results.remove(result)
                            definitions.remove(definition)
                            entries.remove(entry)
                            for field in ("total", "executed", "passed"):
                                counters.set(field, str(147 + 2 - 1))
                        elif mutation == "extra":
                            extra_result, extra_definition, extra_entry = map(copy.deepcopy, (result, definition, entry))
                            test, execution = str(uuid.uuid4()), str(uuid.uuid4())
                            extra_result.set("testId", test)
                            extra_result.set("executionId", execution)
                            extra_result.set("testName", result.get("testName") + " extra")
                            extra_definition.set("id", test)
                            extra_definition.set("name", extra_result.get("testName"))
                            extra_definition.find(NS + "Execution").set("id", execution)
                            extra_entry.set("testId", test)
                            extra_entry.set("executionId", execution)
                            results.append(extra_result)
                            definitions.append(extra_definition)
                            entries.append(extra_entry)
                            for field in ("total", "executed", "passed"):
                                counters.set(field, str(147 + 2 + 1))
                        elif mutation == "unknown":
                            definition.find(NS + "TestMethod").set("name", "UnapprovedAboutCase")
                            name = f'{ABOUT_CTA_CLASS}.UnapprovedAboutCase(culture: "foreign")'
                            result.set("testName", name)
                            definition.set("name", name)
                        elif mutation == "duplicate-sourcecase":
                            name = about_rows[1 - case][0].get("testName")
                            result.set("testName", name)
                            definition.set("name", name)
                        else:
                            result.set("outcome", "Failed" if mutation == "failed" else "NotExecuted")
                            counters.set("passed", str(147 + 2 - 1))
                            counters.set("failed" if mutation == "failed" else "notExecuted", "1")
                        with self.assertRaises(ValueError):
                            self.verify(root, directory, dll, "migration-consumers")

    def test_exact_frozen_roster_counts(self):
        self.assertEqual([10, 10, 9, 26, 4], [
            sum(gate.ROSTERS["fdm"]["Legacy.Maliev.Web.Tests." + cls].values())
            for cls in ("FdmSimulationModelPathsTests", "FdmSimulationSupportTests",
                        "FdmSimulationScenarioTests", "FdmSimulationProfileTests",
                        "FdmOriginalServiceEligibilityBoundaryTests")])
        self.assertEqual(16, sum(next(iter(gate.ROSTERS["sidebar"].values())).values()))
        self.assertEqual({"RealPartsRailExposesLongNamesAndKeyboardActions": 14,
                          "CompactPartsRailKeepsFullNamesAndKeyboardActionsReachable": 2},
                         gate.ROSTERS["sidebar"]["Legacy.Maliev.Web.Tests.InstantQuotationSidebarLayoutBrowserTests"])

    def test_both_complete_suites_accept_lowercase_native_storage(self):
        for suite, total in (("fdm", 59), ("sidebar", 16), ("migration-consumers", 147 + 2), ("billing-lifetime", 50)):
            with self.subTest(suite=suite), tempfile.TemporaryDirectory() as temp:
                directory = Path(temp)
                root, dll = self.fixture(directory, suite)
                self.assertEqual(total, self.verify(root, directory, dll, suite)["passed"])

    def test_every_missing_counter_and_dirty_nonpass_counter_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            baseline, dll = self.fixture(directory, "fdm")
            for field in FIELDS:
                with self.subTest(missing=field):
                    root = copy.deepcopy(baseline)
                    del root.find(NS + "ResultSummary/" + NS + "Counters").attrib[field]
                    with self.assertRaises(ValueError):
                        self.verify(root, directory, dll, "fdm")
                with self.subTest(dirty=field):
                    root = copy.deepcopy(baseline)
                    root.find(NS + "ResultSummary/" + NS + "Counters").set(field, "58" if field in ("total", "executed", "passed") else "1")
                    with self.assertRaises(ValueError):
                        self.verify(root, directory, dll, "fdm")

    def test_identity_join_outcome_namespace_roster_and_assembly_rejections(self):
        mutations = {
            "namespace": lambda r: setattr(r, "tag", "TestRun"),
            "summary": lambda r: r.find(NS + "ResultSummary").set("outcome", "Aborted"),
            "missing-result": lambda r: r.find(NS + "Results").remove(r.find(NS + "Results")[0]),
            "extra-definition": lambda r: ET.SubElement(r.find(NS + "TestDefinitions"), NS + "UnitTest"),
            "duplicate-test-id": lambda r: r.find(NS + "Results")[1].set("testId", r.find(NS + "Results")[0].get("testId")),
            "duplicate-execution-id": lambda r: r.find(NS + "Results")[1].set("executionId", r.find(NS + "Results")[0].get("executionId")),
            "entry-join": lambda r: r.find(NS + "TestEntries")[0].set("executionId", str(uuid.uuid4())),
            "definition-execution": lambda r: r.find(NS + "TestDefinitions")[0].find(NS + "Execution").set("id", str(uuid.uuid4())),
            "outcome": lambda r: r.find(NS + "Results")[0].set("outcome", "Failed"),
            "unknown-class": lambda r: r.find(NS + "TestDefinitions")[0].find(NS + "TestMethod").set("className", "Foreign.Tests"),
            "unknown-method": lambda r: r.find(NS + "TestDefinitions")[0].find(NS + "TestMethod").set("name", "ForeignMethod"),
            "definition-name": lambda r: r.find(NS + "TestDefinitions")[0].set("name", "ForeignName"),
            "foreign-codebase": lambda r: r.find(NS + "TestDefinitions")[0].find(NS + "TestMethod").set("codeBase", "Foreign.dll"),
            "foreign-storage": lambda r: r.find(NS + "TestDefinitions")[0].set("storage", "Foreign.dll"),
        }
        for suite in ("fdm", "billing-lifetime"):
            with tempfile.TemporaryDirectory() as temp:
                directory = Path(temp)
                baseline, dll = self.fixture(directory, suite)
                for label, mutate in mutations.items():
                    with self.subTest(label=label):
                        root = copy.deepcopy(baseline)
                        mutate(root)
                        with self.assertRaises(ValueError):
                            self.verify(root, directory, dll, suite)


    def test_migration_consumer_families_cannot_be_dropped_skipped_or_replaced(self):
        self.assertEqual(147 + 2, sum(sum(methods.values()) for methods in gate.ROSTERS["migration-consumers"].values()))
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            baseline, dll = self.fixture(directory, "migration-consumers")
            for mutation in ("drop", "skip", "replace-family"):
                with self.subTest(mutation=mutation):
                    root = copy.deepcopy(baseline)
                    counters = root.find(NS + "ResultSummary/" + NS + "Counters")
                    if mutation == "drop":
                        for container in ("Results", "TestDefinitions", "TestEntries"):
                            parent = root.find(NS + container)
                            parent.remove(parent[0])
                        for field in ("total", "executed", "passed"):
                            counters.set(field, str(147 + 2 - 1))
                    elif mutation == "skip":
                        root.find(NS + "Results")[0].set("outcome", "NotExecuted")
                        counters.set("passed", str(147 + 2 - 1))
                        counters.set("notExecuted", "1")
                    else:
                        root.find(NS + "TestDefinitions")[0].find(NS + "TestMethod").set(
                            "className", "Legacy.Maliev.Web.Tests.WebSurfaceTests")
                    with self.assertRaises(ValueError):
                        self.verify(root, directory, dll, "migration-consumers")


    def test_hidden_structural_rows_and_infrastructure_notices_are_rejected(self):
        for suite in ("fdm", "sidebar", "migration-consumers", "billing-lifetime"):
            with self.subTest(suite=suite), tempfile.TemporaryDirectory() as temp:
                directory = Path(temp)
                baseline, dll = self.fixture(directory, suite)
                mutations = []
                for name in ("TestRun", "ResultSummary", "Counters", "Results", "TestDefinitions",
                             "TestEntries", "UnitTestResult", "UnitTest", "TestEntry", "TestMethod", "Execution"):
                    mutations.append(("nested-" + name, name, False))
                    mutations.append(("extra-" + name, name, True))
                for name in ("RunInfo", "ErrorInfo"):
                    mutations.append(("nested-" + name, name, False))
                    mutations.append(("direct-" + name, name, True))
                for label, name, direct in mutations:
                    with self.subTest(case=label):
                        root = copy.deepcopy(baseline)
                        parent = root if direct else ET.SubElement(
                            root.find(NS + "ResultSummary"), NS + "Output")
                        row = ET.SubElement(parent, NS + name)
                        row.set("outcome", "Passed")
                        row.text = "Unexpected infrastructure notice"
                        with self.assertRaises(ValueError):
                            self.verify(root, directory, dll, suite)

    def test_empty_native_run_infos_and_standard_output_remain_allowed(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            root, dll = self.fixture(directory, "migration-consumers")
            summary = root.find(NS + "ResultSummary")
            ET.SubElement(summary, NS + "RunInfos")
            output = ET.SubElement(summary, NS + "Output")
            ET.SubElement(output, NS + "StdOut").text = "Ordinary native test output"
            self.assertEqual(147 + 2, self.verify(root, directory, dll, "migration-consumers")["passed"])


    def test_lifecycle_frozen_cases_and_wrong_argument_rejection(self):
        self.assertEqual(50, len(gate.BILLING_LIFETIME_CASES))
        self.assertEqual(50, len(set(gate.BILLING_LIFETIME_CASES)))
        methods = gate.ROSTERS["billing-lifetime"]["Legacy.Maliev.Web.Tests.BillingProofLifetimeTests"]
        self.assertEqual(38, sum(count for name, count in methods.items() if not name.startswith("StartupFailure_")))
        self.assertEqual(12, sum(count for name, count in methods.items() if name.startswith("StartupFailure_")))
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            root, dll = self.fixture(directory, "billing-lifetime")
            result = next(row for row in root.find(NS + "Results")
                          if 'exceptionType: "System.IO.IOException"' in row.get("testName"))
            changed = result.get("testName").replace("System.IO.IOException", "System.IO.OtherException")
            result.set("testName", changed)
            definition = next(row for row in root.find(NS + "TestDefinitions")
                              if row.get("id") == result.get("testId"))
            definition.set("name", gate.native_definition_display(changed))
            with self.assertRaisesRegex(ValueError, "Exact lifecycle control identities"):
                self.verify(root, directory, dll, "billing-lifetime")

    def test_every_cleanup_stage_identity_is_required_and_rejects_mutations(self):
        cls = "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests"
        method = "ChildCleanupFailure_ReportsOnlyItsBoundedStageAndRetainsDependencies"
        arguments = (("identity", "capture-identity", "IOException"),
                     ("graceful-stop", "request-graceful-stop", "IOException"),
                     ("kill", "kill-exact", "IOException"),
                     ("reap", "wait-exit", "TimeoutException"),
                     ("readers", "join-readers", "TimeoutException"),
                     ("reader-close", "close-readers", "IOException"),
                     ("handle-close", "close-handle", "IOException"))
        expected = {f'{cls}.{method}(failure: "{failure}", stage: "{stage}", exceptionType: "{exception}")'
                    for failure, stage, exception in arguments}
        actual = {name for name in gate.BILLING_LIFETIME_CASES if name.startswith(f'{cls}.{method}(')}
        self.assertEqual(expected, actual)
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            baseline, dll = self.fixture(directory, "billing-lifetime")
            self.assertEqual(50, self.verify(baseline, directory, dll, "billing-lifetime")["passed"])
            for required in sorted(expected):
                index = next(i for i, row in enumerate(baseline.find(NS + "Results"))
                             if row.get("testName") == required)
                for mutation in ("missing", "duplicate", "failure", "stage", "exceptionType", "failed", "not-executed"):
                    with self.subTest(identity=required, mutation=mutation):
                        root = copy.deepcopy(baseline)
                        results = root.find(NS + "Results")
                        definitions = root.find(NS + "TestDefinitions")
                        if mutation == "missing":
                            for rows in (results, definitions, root.find(NS + "TestEntries")):
                                rows.remove(rows[index])
                            counters = root.find(NS + "ResultSummary/" + NS + "Counters")
                            for field in ("total", "executed", "passed"):
                                counters.set(field, "49")
                        elif mutation in ("failed", "not-executed"):
                            results[index].set("outcome", "Failed" if mutation == "failed" else "NotExecuted")
                        else:
                            if mutation == "duplicate":
                                changed = next(name for name in sorted(expected) if name != required)
                            else:
                                changed = required.replace(f'{mutation}: "', f'{mutation}: "wrong-')
                            results[index].set("testName", changed)
                            definitions[index].set("name", gate.native_definition_display(changed))
                        with self.assertRaises(ValueError):
                            self.verify(root, directory, dll, "billing-lifetime")

    def test_billing_inline_lifecycle_gate_requires_same_exact_rows_and_rejects_mutations(self):
        workflow = (ROOT / ".github/workflows/billing-persistence.yml").read_text(encoding="utf-8")
        block = workflow.split("      - name: Require all ", 1)[1].split("      - name:", 1)[0]
        source = textwrap.dedent(block.split("python3 -B - <<'PY'\n", 1)[1].rsplit("          PY", 1)[0])
        code = compile(source, "billing-persistence-inline-lifecycle", "exec")
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            baseline, _ = self.fixture(directory, "billing-lifetime")
            results_directory = directory / "billing-lifetime-test-results"
            results_directory.mkdir()
            def validate(root):
                ET.ElementTree(root).write(results_directory / "actual.trx", encoding="utf-8")
                scope = {}
                with contextlib.chdir(directory), contextlib.redirect_stdout(io.StringIO()):
                    exec(code, scope)
                return scope
            self.assertEqual(set(gate.BILLING_LIFETIME_CASES), validate(baseline)["expected"])
            identities = [name for name in gate.BILLING_LIFETIME_CASES if ".ChildCleanupFailure_" in name]
            self.assertEqual(7, len(identities))
            for required in identities:
                index = next(i for i, row in enumerate(baseline.find(NS + "Results"))
                             if row.get("testName") == required)
                for mutation in ("missing", "duplicate", "failure", "stage", "exceptionType", "failed", "not-executed"):
                    with self.subTest(identity=required, mutation=mutation):
                        root = copy.deepcopy(baseline)
                        results = root.find(NS + "Results")
                        if mutation == "missing":
                            results.remove(results[index])
                            counters = root.find(NS + "ResultSummary/" + NS + "Counters")
                            for field in ("total", "executed", "passed"):
                                counters.set(field, "49")
                        elif mutation in ("failed", "not-executed"):
                            results[index].set("outcome", "Failed" if mutation == "failed" else "NotExecuted")
                        elif mutation == "duplicate":
                            results[index].set("testName", next(name for name in identities if name != required))
                        else:
                            results[index].set("testName", required.replace(f'{mutation}: "', f'{mutation}: "wrong-'))
                        with self.assertRaises(SystemExit):
                            validate(root)

    def test_each_1600_sidebar_identity_rejects_missing_duplicate_foreign_and_nonpassed_rows(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            baseline, dll = self.fixture(directory, "sidebar")
            for culture in ("en", "th"):
                required = f'culture: "{culture}", width: 1600)'
                index = next(i for i, row in enumerate(baseline.find(NS + "Results"))
                             if row.get("testName").endswith(required))
                for mutation in ("missing", "duplicate", "foreign-culture", "foreign-viewport", "failed", "not-executed"):
                    with self.subTest(culture=culture, mutation=mutation):
                        root = copy.deepcopy(baseline)
                        results = root.find(NS + "Results")
                        definitions = root.find(NS + "TestDefinitions")
                        entries = root.find(NS + "TestEntries")
                        if mutation == "missing":
                            for rows in (results, definitions, entries):
                                rows.remove(rows[index])
                            counters = root.find(NS + "ResultSummary/" + NS + "Counters")
                            for field in ("total", "executed", "passed"):
                                counters.set(field, "13")
                        elif mutation in ("failed", "not-executed"):
                            results[index].set("outcome", "Failed" if mutation == "failed" else "NotExecuted")
                        else:
                            name = results[index].get("testName")
                            if mutation == "duplicate":
                                name = name.replace("width: 1600", "width: 1280")
                            elif mutation == "foreign-culture":
                                name = name.replace(f'culture: "{culture}"', 'culture: "fr"')
                            else:
                                name = name.replace("width: 1600", "width: 1601")
                            results[index].set("testName", name)
                            definitions[index].set("name", name)
                        with self.assertRaises(ValueError):
                            self.verify(root, directory, dll, "sidebar")

    def test_sidebar_fixture_has_all_fourteen_retained_and_two_exact_compact_names(self):
        with tempfile.TemporaryDirectory() as temp:
            root, _ = self.fixture(Path(temp), "sidebar")
            cls = "Legacy.Maliev.Web.Tests.InstantQuotationSidebarLayoutBrowserTests"
            expected = {f'{cls}.RealPartsRailExposesLongNamesAndKeyboardActions(culture: "{culture}", width: {width})'
                        for culture in ("en", "th") for width in (320, 375, 820, 992, 1024, 1280, 1600)}
            expected.update(f'{cls}.CompactPartsRailKeepsFullNamesAndKeyboardActionsReachable(culture: "{culture}")'
                            for culture in ("en", "th"))
            self.assertEqual(expected, {row.get("testName") for row in root.find(NS + "Results")})

    def test_each_compact_sidebar_identity_rejects_missing_duplicate_foreign_and_nonpassed_rows(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            baseline, dll = self.fixture(directory, "sidebar")
            cls = "Legacy.Maliev.Web.Tests.InstantQuotationSidebarLayoutBrowserTests"
            method = "CompactPartsRailKeepsFullNamesAndKeyboardActionsReachable"
            for culture in ("en", "th"):
                required = f'{cls}.{method}(culture: "{culture}")'
                index = next(i for i, row in enumerate(baseline.find(NS + "Results"))
                             if row.get("testName") == required)
                for mutation in ("missing", "duplicate", "foreign-culture", "foreign-arguments", "foreign-method", "failed", "not-executed"):
                    with self.subTest(culture=culture, mutation=mutation):
                        root = copy.deepcopy(baseline)
                        results = root.find(NS + "Results")
                        definitions = root.find(NS + "TestDefinitions")
                        entries = root.find(NS + "TestEntries")
                        if mutation == "missing":
                            for rows in (results, definitions, entries):
                                rows.remove(rows[index])
                            counters = root.find(NS + "ResultSummary/" + NS + "Counters")
                            for field in ("total", "executed", "passed"):
                                counters.set(field, "13")
                        elif mutation in ("failed", "not-executed"):
                            results[index].set("outcome", "Failed" if mutation == "failed" else "NotExecuted")
                        else:
                            if mutation == "duplicate":
                                other = "th" if culture == "en" else "en"
                                name = f'{cls}.{method}(culture: "{other}")'
                            elif mutation == "foreign-culture":
                                name = f'{cls}.{method}(culture: "fr")'
                            elif mutation == "foreign-arguments":
                                name = f'{cls}.{method}(culture: "{culture}", width: 1280)'
                            else:
                                name = f'{cls}.{method}Unknown(culture: "{culture}")'
                                definitions[index].find(NS + "TestMethod").set("name", method + "Unknown")
                            results[index].set("testName", name)
                            definitions[index].set("name", name)
                        with self.assertRaises(ValueError):
                            self.verify(root, directory, dll, "sidebar")

    def test_unknown_sidebar_viewport_cannot_replace_required_row(self):
        for culture, width in (("en", 320), ("en", 1024), ("th", 1024)):
            with self.subTest(culture=culture, width=width), tempfile.TemporaryDirectory() as temp:
                directory = Path(temp)
                root, dll = self.fixture(directory, "sidebar")
                result = next(row for row in root.find(NS + "Results")
                              if f'culture: "{culture}", width: {width})' in row.get("testName"))
                changed = result.get("testName").replace(f"width: {width}", "width: 999")
                result.set("testName", changed)
                definition = next(row for row in root.find(NS + "TestDefinitions")
                                  if row.get("id") == result.get("testId"))
                definition.set("name", changed)
                with self.assertRaises(ValueError):
                    self.verify(root, directory, dll, "sidebar")


    def long_native_fixture(self, directory):
        root, dll = self.fixture(directory, "migration-consumers")
        definitions = {row.get("id"): row for row in root.find(NS + "TestDefinitions")}
        rows = []
        for result in root.find(NS + "Results"):
            definition = definitions[result.get("testId")]
            method = definition.find(NS + "TestMethod")
            if (method.get("className") == "Legacy.Maliev.Web.Tests.KnowledgeCncMachiningStaticSsrRouteTests"
                    and method.get("name") == "CncMachiningSpecificationRoute_RendersCompleteLocalizedAccessibleDocument"):
                rows.append((result, definition))
        self.assertEqual(2, len(rows))
        for (result, definition), (_, full, discovered) in zip(rows, PINNED_CNC_DISPLAY_CASES, strict=True):
            result.set("testName", full)
            definition.set("name", discovered)
        return root, dll, rows

    def test_frozen_long_native_cnc_displays_join_all147_without_changing_identities(self):
        self.assertEqual([828, 811], [len(full) for _, full, _ in PINNED_CNC_DISPLAY_CASES])
        self.assertEqual([447, 447], [len(discovered) for _, _, discovered in PINNED_CNC_DISPLAY_CASES])
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            root, dll, _ = self.long_native_fixture(directory)
            self.assertEqual(147 + 2, self.verify(root, directory, dll, "migration-consumers")["passed"])

    def test_native_display_uses_exact447_utf16_boundary_and_control_escaping(self):
        cases = [
            ("", ""),
            ("a" * 446, "a" * 446),
            ("a" * 447, "a" * 447),
            ("a" * 448, "a" * 444 + "\u00b7" * 3),
            ("\u0e01" * 448, "\u0e01" * 444 + "\u00b7" * 3),
            ("\r\n\t", r"\r\n\t"),
            (r"\r\n\t", r"\r\n\t"),
            ("\r\n\t" + "a" * 442, r"\r\n\t" + "a" * 438 + "\u00b7" * 3),
            ("a" * 442 + "\U0001f600" + "bb", "a" * 442 + "\U0001f600" + "bb"),
            ("a" * 442 + "\U0001f600" + "bbbb", "a" * 442 + "\U0001f600" + "\u00b7" * 3),
            ("a" * 443 + "\U0001f600" + "bbb", "a" * 443 + "\ud83d" + "\u00b7" * 3),
        ]
        for index, (full, expected) in enumerate(cases):
            with self.subTest(case=index):
                self.assertEqual(expected, gate.native_definition_display(full))

    def test_long_native_definition_rejects_changed_suffix_prefix_and_arbitrary_shortening(self):
        _, full, discovered = PINNED_CNC_DISPLAY_CASES[0]
        mutations = [
            discovered[:-3] + "...",
            "X" + discovered[1:],
            discovered[:443] + "\u00b7" * 3,
            discovered[:-1],
            full,
            full[:300],
        ]
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            for index, changed in enumerate(mutations):
                with self.subTest(case=index):
                    root, dll, rows = self.long_native_fixture(directory)
                    rows[0][1].set("name", changed)
                    with self.assertRaisesRegex(ValueError, "native-definition-display-mismatch"):
                        self.verify(root, directory, dll, "migration-consumers")

    def test_native_display_mapping_keeps_full_result_name_uniqueness(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            root, dll, rows = self.long_native_fixture(directory)
            rows[1][0].set("testName", rows[0][0].get("testName"))
            rows[1][1].set("name", rows[0][1].get("name"))
            with self.assertRaisesRegex(ValueError, "Unknown, duplicate"):
                self.verify(root, directory, dll, "migration-consumers")

    def test_long_native_displays_keep_method_execution_assembly_and_error_guards(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            for mutation in ("method", "class", "execution", "codebase", "storage", "error"):
                with self.subTest(case=mutation):
                    root, dll, rows = self.long_native_fixture(directory)
                    definition = rows[0][1]
                    method = definition.find(NS + "TestMethod")
                    if mutation == "method":
                        method.set("name", "ForeignMethod")
                    elif mutation == "class":
                        method.set("className", "Foreign.Tests")
                    elif mutation == "execution":
                        definition.find(NS + "Execution").set("id", str(uuid.uuid4()))
                    elif mutation == "codebase":
                        method.set("codeBase", "Foreign.dll")
                    elif mutation == "storage":
                        definition.set("storage", "Foreign.dll")
                    else:
                        ET.SubElement(root.find(NS + "ResultSummary"), NS + "ErrorInfo")
                    with self.assertRaises(ValueError):
                        self.verify(root, directory, dll, "migration-consumers")



    def test_native_truncation_cannot_hide_duplicate_definition_display_names(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            root, dll, rows = self.long_native_fixture(directory)
            first = rows[0][0].get("testName")
            second = first[:-1] + ', extraCase: "distinct")'
            self.assertNotEqual(first, second)
            rows[1][0].set("testName", second)
            rows[1][1].set("name", rows[0][1].get("name"))
            with self.assertRaisesRegex(ValueError, "native-definition-display-duplicate"):
                self.verify(root, directory, dll, "migration-consumers")

if __name__ == "__main__":
    unittest.main()
