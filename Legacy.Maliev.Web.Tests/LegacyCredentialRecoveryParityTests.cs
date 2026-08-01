namespace Legacy.Maliev.Web.Tests;

public sealed class LegacyCredentialRecoveryParityTests
{
    [Fact]
    public void CustomerAuthenticationClient_UsesOpaqueExternalAuthChallengesForEveryCredentialFlow()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web.Infrastructure", "CustomerAuthenticationClient.cs"));
        var contracts = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web.Application", "AccountContracts.cs"));

        Assert.Contains("auth/v1/customer-self-service/register", source, StringComparison.Ordinal);
        Assert.Contains("email-confirmation/request", source, StringComparison.Ordinal);
        Assert.Contains("email-confirmation/complete", source, StringComparison.Ordinal);
        Assert.Contains("password-reset/request", source, StringComparison.Ordinal);
        Assert.Contains("password-reset/complete", source, StringComparison.Ordinal);
        Assert.Contains("customer-self-service/{action}", source, StringComparison.Ordinal);
        Assert.Contains("\"email/change\"", source, StringComparison.Ordinal);
        Assert.Contains("\"password/change\"", source, StringComparison.Ordinal);
        Assert.Contains("IServiceAccessTokenProvider", source, StringComparison.Ordinal);
        Assert.Contains("CustomerCredentialOperationResult", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentityEmailTokenCodec", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentityEmailTokenCodec", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RetainedRazorPostBoundaries_DelegateCredentialRecoveryWithoutEchoingSecrets()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var files = new[]
        {
            Path.Combine(web, "Pages", "Account", "ForgotPassword.cshtml.cs"),
            Path.Combine(web, "Pages", "Account", "ResetPassword.cshtml.cs"),
            Path.Combine(web, "Pages", "Account", "Signup.cshtml.cs"),
            Path.Combine(web, "Areas", "Member", "Pages", "Account", "Manage", "ChangeEmail.cshtml.cs"),
            Path.Combine(web, "Areas", "Member", "Pages", "Account", "Manage", "ChangePassword.cshtml.cs")
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.Contains("ICustomerAuthenticationClient", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IdentityEmailTokenCodec", source, StringComparison.Ordinal);
            Assert.DoesNotContain("LogInformation(\"", source, StringComparison.Ordinal);
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
