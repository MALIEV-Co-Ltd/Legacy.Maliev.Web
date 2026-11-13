using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Retains the nineteen-article source content and localization obligations.</summary>
public sealed class KnowledgeLibrarySourceContractTests
{
    public static IEnumerable<object[]> Articles()
    {
        yield return new object[] { "CncCuttingToolsContent.razor", "CncCuttingToolsPage.razor", "CNC/Cutting-Tools.cshtml", "CncCuttingToolsContent" };
        yield return new object[] { "CncFeedsAndSpeedsContent.razor", "CncFeedsAndSpeedsPage.razor", "CNC/Feeds-and-Speeds.cshtml", "CncFeedsAndSpeedsContent" };
        yield return new object[] { "CncGeometryAndWorkholdingContent.razor", "CncGeometryAndWorkholdingPage.razor", "CNC/Geometry-and-Workholding.cshtml", "CncGeometryAndWorkholdingContent" };
        yield return new object[] { "CncHolesAndTolerancesContent.razor", "CncHolesAndTolerancesPage.razor", "CNC/Holes-and-Tolerances.cshtml", "CncHolesAndTolerancesContent" };
        yield return new object[] { "CncMachiningProcessesContent.razor", "CncMachiningProcessesPage.razor", "CNC/Machining-Processes.cshtml", "CncMachiningProcessesContent" };
        yield return new object[] { "FinishingHeatTreatmentContent.razor", "FinishingHeatTreatmentPage.razor", "Finishing/Heat-Treatment.cshtml", "FinishingHeatTreatmentContent" };
        yield return new object[] { "FinishingSurfaceFinishesContent.razor", "FinishingSurfaceFinishesPage.razor", "Finishing/Surface-Finishes.cshtml", "FinishingSurfaceFinishesContent" };
        yield return new object[] { "InspectionMeasuringPartsContent.razor", "InspectionMeasuringPartsPage.razor", "Inspection/Measuring-Parts.cshtml", "InspectionMeasuringPartsContent" };
        yield return new object[] { "ManufacturingProcessSelectionContent.razor", "ManufacturingProcessSelectionPage.razor", "Manufacturing/Process-Selection.cshtml", "ManufacturingProcessSelectionContent" };
        yield return new object[] { "MaterialsEngineeringPlasticsContent.razor", "MaterialsEngineeringPlasticsPage.razor", "Materials/Engineering-Plastics.cshtml", "MaterialsEngineeringPlasticsContent" };
        yield return new object[] { "MaterialsMetalsContent.razor", "MaterialsMetalsPage.razor", "Materials/Metals.cshtml", "MaterialsMetalsContent" };
        yield return new object[] { "MaterialsSelectionContent.razor", "MaterialsSelectionPage.razor", "Materials/Selection.cshtml", "MaterialsSelectionContent" };
        yield return new object[] { "KnowledgeIndexContent.razor", "KnowledgeIndexPage.razor", "Index.cshtml", "KnowledgeIndexContent" };
        yield return new object[] { "Specifications/CncMachiningContent.razor", "Specifications/CncMachiningSpecificationPage.razor", "Specifications/CNC-Machining.cshtml", "CncMachiningContent" };
        yield return new object[] { "GuidelinesContent.razor", "GuidelinesPage.razor", "Guidelines.cshtml", "GuidelinesContent" };
        yield return new object[] { "Specifications/ThreeDimensionalPrintingContent.razor", "Specifications/ThreeDimensionalPrintingSpecificationPage.razor", "Specifications/3D-Printing.cshtml", "ThreeDimensionalPrintingContent" };
        yield return new object[] { "Specifications/ThreeDimensionalScanningContent.razor", "Specifications/ThreeDimensionalScanningSpecificationPage.razor", "Specifications/3D-Scanning.cshtml", "ThreeDimensionalScanningContent" };
        yield return new object[] { "Specifications/SpecificationsIndexContent.razor", "Specifications/SpecificationsIndexPage.razor", "Specifications/Index.cshtml", "SpecificationsIndexContent" };
        yield return new object[] { "WorkflowContent.razor", "WorkflowPage.razor", "Workflow.cshtml", "WorkflowContent" };
    }

    [Theory]
    [MemberData(nameof(Articles))]
    public void EveryArticleKeepsLocalizedStaticContentAndRetainedRenderer(
        string componentPath, string pagePath, string retainedPath, string componentName)
    {
        var project = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var directory = Path.Combine(project, "Components", "Pages", "Knowledges");
        var component = File.ReadAllText(Path.Combine(directory, componentPath));
        var page = File.ReadAllText(Path.Combine(directory, pagePath));
        var retained = File.ReadAllText(Path.Combine(project, "Areas", "Knowledges", "Pages", retainedPath));
        Assert.Contains("class=\"knowledge-document\"", component, StringComparison.Ordinal);
        Assert.Contains($"IStringLocalizer<{componentName}>", component, StringComparison.Ordinal);
        Assert.Contains($"type=\"typeof({componentName})\"", retained, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", retained, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page", component, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "content-container", "Lorem ipsum", "still under development", "style=" })
            Assert.DoesNotContain(forbidden, component, StringComparison.OrdinalIgnoreCase);

        var resourcePath = Path.Combine(project, "Resources", "Components", "Pages", "Knowledges",
            Path.ChangeExtension(componentPath, ".th.resx"));
        var entries = XDocument.Load(resourcePath).Root!.Elements("data").ToArray();
        var keys = entries.Select(entry => (string)entry.Attribute("name")!).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        var references = Regex.Matches(component,
            """Localizer\["([^"]+)"\]""").Cast<Match>()
            .Concat(Regex.Matches(page + retained,
                """(?:ContentLocalizer|MetadataLocalizer)\["([^"]+)"\]""").Cast<Match>()).ToArray();
        Assert.NotEmpty(references);
        foreach (var reference in references)
        {
            var key = reference.Groups[1].Value;
            var entry = Assert.Single(entries, candidate => (string?)candidate.Attribute("name") == key);
            Assert.False(string.IsNullOrWhiteSpace(entry.Element("value")?.Value),
                $"Missing Thai translation in {componentPath}: {key}");
        }
    }

    [Fact]
    public void EveryArticleKeepsItsOwnBoundedPhotoAndFullSizeTeachingDiagrams()
    {
        var project = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var photos = new HashSet<string>(StringComparer.Ordinal);
        var articles = Articles().ToArray();
        Assert.Equal(19, articles.Length);
        foreach (var article in articles)
        {
            var componentPath = (string)article[0];
            var component = File.ReadAllText(Path.Combine(project, "Components", "Pages", "Knowledges", componentPath));
            var photo = Regex.Match(component,
                "<figure class=\"docs-figure knowledge-photo\">\\s*<img src=\"/(?<asset>[^\"]+\\.webp)\"");
            Assert.True(photo.Success, $"Missing article photograph: {componentPath}");
            var asset = photo.Groups["asset"].Value;
            Assert.True(photos.Add(asset), $"Repeated article photograph: {componentPath}");
            Assert.InRange(new FileInfo(Path.Combine(project, "wwwroot", asset)).Length, 1L, 400000L);
            if (componentPath != "KnowledgeIndexContent.razor")
            {
                Assert.Contains("knowledge-diagram-viewport", component, StringComparison.Ordinal);
                Assert.Contains("Open full-size diagram", component, StringComparison.Ordinal);
            }
        }

        Assert.Equal(19, photos.Count);
        var css = File.ReadAllText(Path.Combine(project, "wwwroot", "src", "app", "css", "application-shell.css"));
        foreach (var selector in new[] { ".knowledge-document", ".docs-link-card", ".docs-step-list" })
            Assert.Contains(selector, css, StringComparison.Ordinal);
    }


    [Fact]
    public void SharedSidebarKeepsUniqueNonemptyThaiValuesForEveryNavigationKey()
    {
        var project = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var component = File.ReadAllText(Path.Combine(project, "Components", "Pages", "Knowledges", "KnowledgeNavigation.razor"));
        var retained = File.ReadAllText(Path.Combine(project, "Areas", "Knowledges", "Pages", "Shared", "_SidebarPartial.cshtml"));
        Assert.Contains("type=\"typeof(KnowledgeNavigation)\"", retained, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", retained, StringComparison.Ordinal);
        var entries = XDocument.Load(Path.Combine(project, "Resources", "Components", "Pages", "Knowledges",
            "KnowledgeNavigation.th.resx")).Root!.Elements("data").ToArray();
        var keys = entries.Select(entry => (string)entry.Attribute("name")!).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        // Group/item titles are localized indirectly; retain the source guard for Title assignments too.
        var references = Regex.Matches(component, "(?:Localizer\\[|Title = )\"([^\"]+)\"");
        Assert.NotEmpty(references.Cast<Match>());
        foreach (var reference in references.Cast<Match>())
        {
            var key = reference.Groups[1].Value;
            var entry = Assert.Single(entries, candidate => (string?)candidate.Attribute("name") == key);
            Assert.False(string.IsNullOrWhiteSpace(entry.Element("value")?.Value),
                $"Missing Thai navigation translation: {key}");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
