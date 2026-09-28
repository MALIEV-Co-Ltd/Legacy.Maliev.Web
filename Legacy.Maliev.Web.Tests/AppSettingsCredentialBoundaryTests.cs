using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class AppSettingsCredentialBoundaryTests
{
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void CheckedInWebConfigurationHasNoConnectionStringsSection(string fileName)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "Legacy.Maliev.Web", fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.DoesNotContain(
            document.RootElement.EnumerateObject(),
            property => string.Equals(property.Name, "ConnectionStrings", StringComparison.OrdinalIgnoreCase));
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
