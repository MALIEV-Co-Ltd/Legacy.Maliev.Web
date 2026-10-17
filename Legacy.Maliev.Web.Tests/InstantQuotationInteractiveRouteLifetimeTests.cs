using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationInteractiveRouteLifetimeTests
{
    [Fact]
    public async Task ActualCasesOwnIndependentPlainFactoriesAndReleaseEachCase()
    {
        using var first = new InstantQuotationInteractiveRouteTests();
        using var second = new InstantQuotationInteractiveRouteTests();
        var firstParent = first.LifetimeParent;
        var firstChild = first.LifetimeFactory;
        var firstClient = first.LifetimeClient;
        var secondParent = second.LifetimeParent;
        var secondChild = second.LifetimeFactory;
        var secondClient = second.LifetimeClient;

        Assert.IsType<WebApplicationFactory<Program>>(firstParent);
        Assert.IsType<WebApplicationFactory<Program>>(secondParent);
        Assert.NotSame(firstParent, secondParent);
        Assert.NotSame(firstChild, secondChild);
        Assert.NotSame(firstClient, secondClient);
        Assert.Same(firstChild, Assert.Single(firstParent.Factories));
        Assert.Same(secondChild, Assert.Single(secondParent.Factories));
        Assert.Equal(3, first.RemainingOwnedResources);
        Assert.Equal(3, second.RemainingOwnedResources);
        Assert.Equal(new Uri("https://localhost"), firstClient.BaseAddress);
        Assert.Equal("Testing", firstChild.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        var configuration = firstChild.Services.GetRequiredService<IConfiguration>();
        Assert.Equal("test-project", configuration["Recaptcha:ProjectId"]);
        Assert.Equal("test-site-key", configuration["Recaptcha:SiteKey"]);

        first.Dispose();
        first.Dispose();
        Assert.Equal(0, first.RemainingOwnedResources);
        AssertDisposedFactory(firstChild);
        AssertDisposedFactory(firstParent);
        Assert.Throws<ObjectDisposedException>(() => first.LifetimeParent);
        Assert.Throws<ObjectDisposedException>(() => first.LifetimeFactory);
        Assert.Throws<ObjectDisposedException>(() => first.LifetimeClient);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => firstClient.GetAsync("/legal?culture=en"));

        using var response = await secondClient.GetAsync("/legal?culture=en");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Same(secondParent, second.LifetimeParent);
        Assert.Same(secondChild, second.LifetimeFactory);
        Assert.Equal(3, second.RemainingOwnedResources);
    }

    [Fact]
    public void PartialConstructionReleasesBothOwnedFactoriesAndPreservesOriginalFailure()
    {
        WebApplicationFactory<Program>? parent = null;
        WebApplicationFactory<Program>? child = null;
        var original = new InvalidOperationException("Synthetic pre-client construction failure.");
        var actual = Assert.Throws<InvalidOperationException>(() =>
            new InstantQuotationInteractiveRouteTests((ownedParent, ownedChild) =>
            {
                parent = ownedParent;
                child = ownedChild;
                Assert.Same(ownedChild, Assert.Single(ownedParent.Factories));
                throw original;
            }));

        Assert.Same(original, actual);
        Assert.NotNull(parent);
        Assert.NotNull(child);
        AssertDisposedFactory(child);
        AssertDisposedFactory(parent);
    }

    private static void AssertDisposedFactory(WebApplicationFactory<Program> factory)
    {
        // Pinned Microsoft.AspNetCore.Mvc.Testing 10.0.12 sets this flag after disposal.
        // Reading it avoids starting a new unowned server through a disposed-factory probe.
        var disposed = typeof(WebApplicationFactory<Program>).GetField("_disposed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(disposed);
        Assert.True(Assert.IsType<bool>(disposed.GetValue(factory)));
    }
}
