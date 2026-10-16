using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actual per-instance account host ownership; no Auth producer or quotation qualification.</summary>
public sealed class AccountBoundaryOwnedLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualAccountCasesOwnIndependentParentsAndOneDisposalLeavesTheOtherUsable(bool forgot)
    {
        var first = Create(forgot);
        var second = Create(forgot);
        try
        {
            await first.Lifetime.InitializeAsync();
            await second.Lifetime.InitializeAsync();
            var firstParent = first.Parent();
            var secondParent = second.Parent();
            Assert.NotSame(firstParent, secondParent);
            await using var firstChild = first.Child();
            await using var secondChild = second.Child();
            using var firstClient = first.Client(firstChild);
            using var secondClient = second.Client(secondChild);
            Assert.Equal(new Uri("https://www.maliev.com"), firstClient.BaseAddress);
            Assert.Equal(new Uri("https://www.maliev.com"), secondClient.BaseAddress);
            using (var response = await firstClient.GetAsync("/account/forgotpassword?culture=en"))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using (var response = await secondClient.GetAsync("/account/forgotpassword?culture=th"))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(firstParent.Factories);
            Assert.Single(secondParent.Factories);

            await first.Lifetime.DisposeAsync();
            await first.Lifetime.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => first.Parent());
            using var firstChildProbe = first.Client(firstChild);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => firstChildProbe.GetAsync("/account/forgotpassword"));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => firstClient.GetAsync("/account/forgotpassword"));
            Assert.Same(secondParent, second.Parent());
            using var stillUsable = await secondClient.GetAsync("/account/forgotpassword?culture=en");
            Assert.Equal(HttpStatusCode.OK, stillUsable.StatusCode);
        }
        finally
        {
            try { await first.Lifetime.DisposeAsync(); }
            finally { await second.Lifetime.DisposeAsync(); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedBodyWithSuccessfulCleanup_ReleasesOwnedResourcesAndPreservesPrimary(bool forgot)
    {
        var owner = Create(forgot);
        try
        {
            await owner.Lifetime.InitializeAsync();
            await using var child = owner.Child();
            using var client = owner.Client(child);
            var original = new InvalidOperationException("Synthetic account test-body failure.");
            var actual = await Record.ExceptionAsync(async () =>
            {
                try
                {
                    using var response = await client.GetAsync("/account/forgotpassword?culture=en");
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    throw original;
                }
                finally { await owner.Lifetime.DisposeAsync(); }
            });
            Assert.Same(original, actual);
            Assert.Throws<ObjectDisposedException>(() => owner.Parent());
            using var childProbe = owner.Client(child);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => childProbe.GetAsync("/account/forgotpassword"));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAsync("/account/forgotpassword"));
        }
        finally { await owner.Lifetime.DisposeAsync(); }
    }

    private static AccountCase Create(bool forgot)
    {
        if (forgot)
        {
            var owner = new PublicAccountWorkflowTestsForgotPasswordBoundary();
            return new(owner, () => owner.LifetimeParent, owner.CreateLifetimeChild,
                PublicAccountWorkflowTestsForgotPasswordBoundary.CreateLifetimeClient);
        }

        var reset = new PublicAccountWorkflowTestsPasswordResetBoundary();
        return new(reset, () => reset.LifetimeParent, reset.CreateLifetimeChild,
            PublicAccountWorkflowTestsPasswordResetBoundary.CreateLifetimeClient);
    }

    private sealed record AccountCase(
        IAsyncLifetime Lifetime,
        Func<OwnedWebSurfaceParent> Parent,
        Func<WebApplicationFactory<Program>> Child,
        Func<WebApplicationFactory<Program>, HttpClient> Client);
}
