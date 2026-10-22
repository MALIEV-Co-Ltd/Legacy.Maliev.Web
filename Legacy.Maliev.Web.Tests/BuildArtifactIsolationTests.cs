using System.Xml.Linq;

namespace Legacy.Maliev.Web.Tests;

/// <summary>
/// Guards compiler-generated Web documentation output without adding generated XML to source control.
/// </summary>
public sealed class BuildArtifactIsolationTests
{
    /// <summary>
    /// Preserves Web XML generation and prevents source-root documentation artifacts in every canonical project.
    /// </summary>
    [Fact]
    public void WebDocumentationFile_IsGeneratedOnlyInBuildOutput()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectDirectory = Path.Combine(repositoryRoot, "Legacy.Maliev.Web");
        var project = XDocument.Load(Path.Combine(projectDirectory, "Legacy.Maliev.Web.csproj"));
        var generationSettings = project.Descendants("GenerateDocumentationFile")
            .Select(element => element.Value.Trim()).ToArray();

        Assert.Contains("true", generationSettings, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(project.Descendants("DocumentationFile"));
        Assert.DoesNotContain(project.Descendants("None"), element =>
            string.Equals((string?)element.Attribute("Update"), "Legacy.Maliev.Web.xml", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(projectDirectory, "Legacy.Maliev.Web.xml")));

        var projects = FindProjectFiles(repositoryRoot).ToArray();
        Assert.NotEmpty(projects);
        foreach (var projectPath in projects)
        {
            var configuration = XDocument.Load(projectPath);
            Assert.Empty(configuration.Descendants("DocumentationFile"));
            Assert.DoesNotContain(configuration.Descendants("None"), element =>
                ((string?)element.Attribute("Update"))?.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) == true);
            var directory = Path.GetDirectoryName(projectPath)!;
            var assemblyNames = configuration.Descendants("AssemblyName")
                .Select(element => element.Value.Trim())
                .Append(Path.GetFileNameWithoutExtension(projectPath))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var assemblyName in assemblyNames)
            {
                Assert.False(File.Exists(Path.Combine(directory, assemblyName + ".xml")), projectPath);
            }
        }
    }

    /// <summary>
    /// Reads real compiler-emitted XML next to the loaded Web assembly and verifies the retained public and private member descriptions.
    /// </summary>
    /// <param name="memberId">The exact compiler member identifier for the mapped Web method.</param>
    /// <param name="expectedSummaryFragment">The source contract text required in the emitted summary.</param>
    [Theory]
    [InlineData("M:Legacy.Maliev.Web.CanonicalUrlPolicy.GetCanonicalUrl(Microsoft.AspNetCore.Http.PathString)", "Builds an absolute canonical URL")]
    [InlineData("M:Legacy.Maliev.Web.Pages.InstantQuotation.ThreeDimensionalPrinting.RetryJson", "without losing uploaded parts or wizard state")]
    [InlineData("M:Legacy.Maliev.Web.Pages.InstantQuotation.ThreeDimensionalPrinting.TerminalOrRedirect(System.Boolean)", "must not invite blind resubmission")]
    public void WebDocumentationFile_ContainsCompiledAssemblyAndPublicMember(string memberId, string expectedSummaryFragment)
    {
        var assembly = typeof(Program).Assembly;
        var documentationPath = Path.ChangeExtension(assembly.Location, ".xml");
        var sourceArtifactPath = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "Legacy.Maliev.Web.xml");

        Assert.True(File.Exists(documentationPath), "The Web assembly's compiler-generated XML documentation is missing.");
        Assert.NotEqual(Path.GetFullPath(sourceArtifactPath), Path.GetFullPath(documentationPath));

        var documentation = XDocument.Load(documentationPath);
        Assert.Equal(assembly.GetName().Name, documentation.Root?.Element("assembly")?.Element("name")?.Value);
        var member = Assert.Single(documentation.Descendants("member"), element =>
            (string?)element.Attribute("name") == memberId);
        Assert.False(string.IsNullOrWhiteSpace(member.Element("summary")?.Value));
        Assert.Contains(expectedSummaryFragment, member.Element("summary")?.Value ?? string.Empty, StringComparison.Ordinal);
    }

    private static IEnumerable<string> FindProjectFiles(string directory)
    {
        foreach (var project in Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly))
        {
            yield return project;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (name is "bin" or "obj" or "node_modules" or ".git" or ".dependencies" or "TestResults" or "artifacts" or ".artifacts"
                || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            foreach (var project in FindProjectFiles(child))
            {
                yield return project;
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Unable to locate the Legacy.Maliev.Web repository root.");
    }
}
