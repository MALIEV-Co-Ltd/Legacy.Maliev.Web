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

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void CheckedInWebConfigurationDoesNotEmbedCredentialMaterial(string fileName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", fileName)));
        AssertCredentialFieldsEmpty(document.RootElement);
    }

    private static void AssertCredentialFieldsEmpty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var credentialNames = new[]
                {
                    "ClientSecret", "ServiceAccount", "PrivateKey", "Password",
                    "CertificatePassword", "CertificatePfxBase64", "CredentialsPath", "EmbedApiKey"
                };
                if (credentialNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    Assert.True(property.Value.ValueKind == JsonValueKind.Null
                        || (property.Value.ValueKind == JsonValueKind.String
                            && string.IsNullOrWhiteSpace(property.Value.GetString())),
                        $"Checked-in credential field {property.Name} must be empty.");
                }
                AssertCredentialFieldsEmpty(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) AssertCredentialFieldsEmpty(item);
        }
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
