using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerQuotationSurfaceContractTests
{
    [Fact]
    public void CustomerQuotationBoundary_IsRegisteredAndPreservesMemberRoutesWithoutPaymentMutation()
    {
        var application = typeof(ICustomerAccountClient).Assembly;
        var infrastructure = typeof(CustomerAccountClient).Assembly;
        var repositoryRoot = FindRepositoryRoot();
        var registration = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Legacy.Maliev.Web.Infrastructure",
            "ServiceCollectionExtensions.cs"));

        Assert.NotNull(application.GetType("Legacy.Maliev.Web.Application.ICustomerQuotationClient"));
        Assert.NotNull(infrastructure.GetType("Legacy.Maliev.Web.Infrastructure.CustomerQuotationClient"));
        Assert.Contains(
            "AddScoped<ICustomerQuotationClient, CustomerQuotationClient>()",
            registration,
            StringComparison.Ordinal);
        Assert.True(PageExists(repositoryRoot, "Index.cshtml"));
        Assert.True(PageExists(repositoryRoot, "View.cshtml"));
        Assert.False(PageExists(repositoryRoot, "PaymentSuccess.cshtml"));
        Assert.False(PageExists(repositoryRoot, "PaymentSuccess.cshtml.cs"));
        var indexModel = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Legacy.Maliev.Web",
            "Areas",
            "Member",
            "Pages",
            "Quotations",
            "Index.cshtml.cs"));
        var detailModel = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Legacy.Maliev.Web",
            "Areas",
            "Member",
            "Pages",
            "Quotations",
            "View.cshtml.cs"));
        Assert.Contains("[Authorize]", indexModel, StringComparison.Ordinal);
        Assert.Contains("GetCustomerDatabaseIdAsync", indexModel, StringComparison.Ordinal);
        Assert.Contains("[Authorize]", detailModel, StringComparison.Ordinal);
        Assert.Contains("GetCustomerDatabaseIdAsync", detailModel, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPost", detailModel, StringComparison.Ordinal);
        var retiredPaymentResult = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Legacy.Maliev.Web",
            "MemberCompatibilityEndpointRouteBuilderExtensions.cs"));
        Assert.Contains("/member/quotations/paymentsuccess", retiredPaymentResult, StringComparison.Ordinal);
        Assert.Contains("/member/quotations", retiredPaymentResult, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization", retiredPaymentResult, StringComparison.Ordinal);
        Assert.DoesNotContain("MapPost", retiredPaymentResult, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", retiredPaymentResult, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invoice", retiredPaymentResult, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("receipt", retiredPaymentResult, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymentId", retiredPaymentResult, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "paypal",
            string.Join('\n', Directory.EnumerateFiles(
                    Path.Combine(repositoryRoot, "Legacy.Maliev.Web"),
                    "*",
                    SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => Path.GetExtension(path) is ".cs" or ".cshtml" or ".resx")
                .Select(File.ReadAllText)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PageExists(string repositoryRoot, string fileName) => File.Exists(Path.Combine(
        repositoryRoot,
        "Legacy.Maliev.Web",
        "Areas",
        "Member",
        "Pages",
        "Quotations",
        fileName));

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
