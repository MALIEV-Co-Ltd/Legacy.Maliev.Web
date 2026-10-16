namespace Legacy.Maliev.Web.Tests;

public sealed class NativeLoggingMigrationTests
{
    [Fact]
    public void RuntimeInventory_DoesNotReferenceRetiredOrSharedLogging()
    {
        var root = FindRepositoryRoot();
        string[] projects = ["Legacy.Maliev.Web", "Legacy.Maliev.Web.Application", "Legacy.Maliev.Web.Infrastructure"];
        string[] retiredReferences =
        [
            "NLog", "Maliev.LoggerService", "Maliev.NativeLogging",
            "AddMalievJsonConsole", "UseMalievProductionExceptionHandler"
        ];

        var files = projects
            .SelectMany(project => EnumerateProductionInventory(Path.Combine(root, project)))
            .Concat(EnumerateRootContainerRecipes(root))
            .Where(IsRuntimeInventoryFile)
            .ToArray();

        Assert.NotEmpty(files);
        var offenders = files
            .Where(path =>
            {
                var content = File.ReadAllText(path);
                return retiredReferences.Any(reference => content.Contains(reference, StringComparison.OrdinalIgnoreCase));
            })
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ProductionInventory_PrunesExcludedDirectoriesBeforeVisitingThem()
    {
        var root = Directory.CreateTempSubdirectory("legacy-web-logging-inventory-");
        try
        {
            string[] directories =
            [
                "App", "App/nested", "App/bin/deep", "App/OBJ/deep", "docs/deep",
                ".git/deep", "App.Tests/deep", "App/Sub.Tests/deep", "App.TestsSupport"
            ];
            foreach (var directory in directories)
            {
                var folder = Directory.CreateDirectory(Path.Combine(root.FullName, directory));
                File.WriteAllText(Path.Combine(folder.FullName, "sample.cs"), "// Synthetic inventory fixture");
            }

            var visited = new List<string>();
            var actual = EnumerateProductionInventory(root.FullName, directory =>
                    visited.Add(Path.GetRelativePath(root.FullName, directory).Replace('\\', '/')))
                .Select(path => Path.GetRelativePath(root.FullName, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(["App.TestsSupport/sample.cs", "App/nested/sample.cs", "App/sample.cs"], actual);
            Assert.Equal([".", "App", "App.TestsSupport", "App/nested"], visited.Order(StringComparer.Ordinal));
        }
        finally
        {
            var temporaryParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                + Path.DirectorySeparatorChar;
            Assert.StartsWith(temporaryParent, Path.GetFullPath(root.FullName), StringComparison.OrdinalIgnoreCase);
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("Program.cs", true)]
    [InlineData("Web.CSPROJ", true)]
    [InlineData("logging.config", true)]
    [InlineData("Dockerfile", true)]
    [InlineData("Dockerfile.bff", true)]
    [InlineData("dockerfile.compat", true)]
    [InlineData("notes.md", false)]
    [InlineData("bundle.js", false)]
    public void RuntimeInventoryFileSelection_PreservesSourceContract(string name, bool expected)
    {
        Assert.Equal(expected, IsRuntimeInventoryFile(name));
    }

    [Fact]
    public void RootContainerInventory_IncludesLowercaseRecipesButNotOtherFilesOrNestedTrees()
    {
        var root = Directory.CreateTempSubdirectory("legacy-web-container-inventory-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Dockerfile"), "# Synthetic inventory fixture");
            File.WriteAllText(Path.Combine(root.FullName, "dockerfile.compat"), "# Synthetic inventory fixture");
            File.WriteAllText(Path.Combine(root.FullName, "notes.cs"), "// Synthetic inventory fixture");
            var nested = Directory.CreateDirectory(Path.Combine(root.FullName, "nested"));
            File.WriteAllText(Path.Combine(nested.FullName, "Dockerfile"), "# Synthetic inventory fixture");

            var actual = EnumerateRootContainerRecipes(root.FullName)
                .Select(path => Path.GetRelativePath(root.FullName, path))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(["Dockerfile", "dockerfile.compat"], actual);
        }
        finally
        {
            var temporaryParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                + Path.DirectorySeparatorChar;
            Assert.StartsWith(temporaryParent, Path.GetFullPath(root.FullName), StringComparison.OrdinalIgnoreCase);
            root.Delete(recursive: true);
        }
    }

    private static IEnumerable<string> EnumerateRootContainerRecipes(string root) =>
        Directory.EnumerateFiles(root).Where(path =>
            Path.GetFileName(path).StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> EnumerateProductionInventory(string root, Action<string>? visited = null)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            visited?.Invoke(directory);
            foreach (var file in Directory.EnumerateFiles(directory)) yield return file;
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!IsExcludedInventoryDirectory(Path.GetFileName(child))) pending.Push(child);
            }
        }
    }

    private static bool IsExcludedInventoryDirectory(string name) =>
        name.Equals(".git", StringComparison.OrdinalIgnoreCase)
        || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
        || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
        || name.Equals("docs", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase);

    private static bool IsRuntimeInventoryFile(string path) =>
        new[] { ".cs", ".csproj", ".config" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
        || Path.GetFileName(path).StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase);

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
