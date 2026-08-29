namespace Legacy.Maliev.Web.Tests;

public sealed class WebApplicationFactoryLifecycleTests
{
    private static readonly System.Text.RegularExpressions.Regex ConstructorOwnedFactoryPattern = new(
        @"public\s+\w+\s*\([^)]*factory[^)]*\)\s*\{(?:(?!\n\s*\[).)*WithWebHostBuilder",
        System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex DeterministicFactoryOwnerPattern = new(
        @"class\s+\w+[^\n]*\bIDisposable\b(?s:.*)\b(?:factory|configuredFactory)\.Dispose\(\);",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    [Fact]
    public void NonInstantQuotationWebHostTests_UseAnXunitOwnedConfiguredFactory()
    {
        var testDirectory = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web.Tests");
        var violations = Directory
            .EnumerateFiles(testDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).StartsWith("InstantQuotation", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).Equals(
                nameof(WebApplicationFactoryLifecycleTests) + ".cs",
                StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(
                "IClassFixture<WebApplicationFactory<Program>>",
                StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Each configured WebApplicationFactory must be the xUnit-owned class fixture so its host is disposed after the class. Violations: {string.Join(", ", violations)}");
    }

    [Fact]
    public void NonInstantQuotationWebHostTests_DoNotCreateAnUndisposedFactoryInTheirConstructor()
    {
        var testDirectory = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web.Tests");
        var violations = Directory
            .EnumerateFiles(testDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).StartsWith("InstantQuotation", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).Equals(
                nameof(WebApplicationFactoryLifecycleTests) + ".cs",
                StringComparison.Ordinal))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return ConstructorOwnedFactoryPattern.IsMatch(source)
                    && !DeterministicFactoryOwnerPattern.IsMatch(source);
            })
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Do not clone a WebApplicationFactory in a per-test constructor. Put the configuration on the xUnit-owned fixture instead. Violations: {string.Join(", ", violations)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
