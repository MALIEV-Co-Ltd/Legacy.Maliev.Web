namespace Legacy.Maliev.Web.Tests;

public sealed class CncSubmissionRouteContractTests
{
    [Fact]
    public void PublicCncPost_DispatchesTheExactSingleSubmitRequestHandler()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "Program.cs"));

        Assert.Contains("requestedHandler.Count != 1", program, StringComparison.Ordinal);
        Assert.Contains("string.Equals(requestedHandler[0], \"SubmitRequest\", StringComparison.OrdinalIgnoreCase)", program, StringComparison.Ordinal);
        Assert.Contains("submissionHandler.HandleAsync(context)", program, StringComparison.Ordinal);
        Assert.Contains("RequireAntiforgeryTokenAttribute(true)", program, StringComparison.Ordinal);
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
