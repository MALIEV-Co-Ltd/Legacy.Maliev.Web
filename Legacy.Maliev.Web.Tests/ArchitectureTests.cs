namespace Legacy.Maliev.Web.Tests;

using System.Xml.Linq;

public sealed class ArchitectureTests
{
    [Fact]
    public void WebProject_HasNoDirectDatabaseOrLegacyMonolithReferences()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Legacy.Maliev.Web.csproj"));

        // Shared ServiceDefaults uses EF health-check types at runtime. The Web host may carry
        // those runtime assemblies, but it must not reference a DbContext or a database project.
        var projectReferences = string.Join(
            Environment.NewLine,
            project.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("ProjectReference", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain("EntityFrameworkCore", projectReferences, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DbContext", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Maliev.Service.PayPal", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Maliev.LoggerService", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Maliev.PdfService", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultHttpClients_UsePlatformCertificateValidation()
    {
        var root = FindRepositoryRoot();
        var applicationDirectories = new[]
        {
            Path.Combine(root, "Legacy.Maliev.Web"),
            Path.Combine(root, "Legacy.Maliev.Web.Application"),
            Path.Combine(root, "Legacy.Maliev.Web.Infrastructure")
        };
        var source = string.Join(
            Environment.NewLine,
            applicationDirectories
                .SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

        Assert.DoesNotContain("ServerCertificateCustomValidationCallback", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DangerousAcceptAnyServerCertificateValidator", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HttpResiliencePackage_UsesOneVersionAcrossRuntimeProjects()
    {
        var root = FindRepositoryRoot();
        var projectFiles = new[]
        {
            Path.Combine(root, "Legacy.Maliev.Web", "Legacy.Maliev.Web.csproj"),
            Path.Combine(root, "Legacy.Maliev.Web.Infrastructure", "Legacy.Maliev.Web.Infrastructure.csproj")
        };

        var versions = projectFiles
            .Select(path => XDocument.Load(path)
                .Descendants("PackageReference")
                .Single(element => string.Equals(
                    (string?)element.Attribute("Include"),
                    "Microsoft.Extensions.Http.Resilience",
                    StringComparison.Ordinal))
                .Attribute("Version")?.Value)
            .ToArray();

        Assert.All(versions, version => Assert.Equal("10.9.0", version));
        Assert.Single(versions.Distinct(StringComparer.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
