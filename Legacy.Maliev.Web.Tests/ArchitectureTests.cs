namespace Legacy.Maliev.Web.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void WebProject_HasNoDirectDatabaseOrLegacyMonolithReferences()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Legacy.Maliev.Web.csproj"));

        Assert.DoesNotContain("EntityFrameworkCore", project, StringComparison.OrdinalIgnoreCase);
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
