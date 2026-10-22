using System.Net;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Uses the existing browser and real Development Redis host to exercise the local quotation preview.</summary>
/// <param name="browser">The shared, owned browser fixture.</param>
/// <param name="redis">The existing Redis and certificate fixture for actual Development startup.</param>
[Collection(CncNativeBrowserCollection.Name)]
public sealed class DevelopmentCustomerFixtureBrowserTests(CncNativeBrowserFixture browser, RedisRuntimeFixture redis)
    : IClassFixture<RedisRuntimeFixture>
{
    /// <summary>Requires the real zero-file preview and retry to preserve the editable form without commercial effects.</summary>
    /// <param name="culture">The actual rendered English or Thai locale.</param>
    /// <returns>A task representing the authenticated local-UI browser journey.</returns>
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ActualDevelopment_ZeroFilePreviewAndRejectedSubmitKeepDraft(string culture)
    {
        var effects = new DevelopmentFixtureEffects();
        using var certificate = redis.CreateCertificate();
        await using var application = DevelopmentFixtureEffects.CreateFactory(redis, effects);
        application.UseKestrel(options => options.Listen(IPAddress.Loopback, 0,
            listen => listen.UseHttps(certificate)));
        application.StartServer();
        var origin = Assert.Single(application.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses);
        Assert.StartsWith("https://", origin, StringComparison.Ordinal);
        await using var context = await browser.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
        });
        await using var page = await context.NewPageAsync();
        await page.GotoAsync(origin + "/Account/Login?culture=" + culture);
        var loginConsent = page.Locator("#cookieConsent");
        await Assertions.Expect(loginConsent).ToBeVisibleAsync();
        await loginConsent.Locator("[data-consent-action='reject']").ClickAsync();
        await Assertions.Expect(loginConsent).ToBeHiddenAsync();
        await page.Locator("[data-development-customer-fixture] button").ClickAsync();
        await page.WaitForURLAsync("**/InstantQuotation/3D-Printing");
        await page.GotoAsync(origin + "/InstantQuotation/3D-Printing?culture=" + culture);
        Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId !== undefined");
        var quotationUrl = page.Url;
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) await consent.ClickAsync();
        Assert.Equal("empty", await page.Locator(".instant-quote__workflow").GetAttributeAsync("data-workflow-state"));
        await page.Locator("[data-development-customer-preview]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-workflow-customer-details]")).ToBeVisibleAsync();
        Assert.True(await page.Locator("[data-workflow-customer-details]").IsVisibleAsync());
        Assert.Equal("customerdetails", await page.Locator(".instant-quote__workflow").GetAttributeAsync("data-workflow-state"));
        Assert.Equal("ทดสอบ", await page.Locator("[name=FirstName]").InputValueAsync());
        Assert.Equal("ลูกค้า", await page.Locator("[name=LastName]").InputValueAsync());
        Assert.Equal("customer.fixture@localhost", await page.Locator("[name=Email]").InputValueAsync());
        Assert.True(await page.Locator("[name=FirstName]").EvaluateAsync<bool>("input => input.readOnly"));
        Assert.True(await page.Locator("[name=BillingStreet1]").EvaluateAsync<bool>("input => input.readOnly"));
        Assert.False(await page.Locator("[name=Company]").EvaluateAsync<bool>("input => input.readOnly"));
        Assert.True(await page.Locator("#instant-quote-ship-to-billing").IsCheckedAsync());
        Assert.False(await page.Locator("#instant-quote-ship-to-billing").IsDisabledAsync());
        await page.Locator("[name=Company]").FillAsync("Local draft company");
        await page.Locator("[name=Description]").FillAsync("Local draft project");
        await page.Locator("[data-instant-quote-submit]").ClickAsync();
        await page.Locator("[data-instant-quote-submit-feedback]").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
        });
        Assert.Contains("Development fixture mode stops here", await page.Locator(
            "[data-instant-quote-submit-feedback]").InnerTextAsync(), StringComparison.Ordinal);
        Assert.Equal(quotationUrl, page.Url);
        Assert.Equal("Local draft company", await page.Locator("[name=Company]").InputValueAsync());
        Assert.Equal("Local draft project", await page.Locator("[name=Description]").InputValueAsync());
        Assert.False(await page.Locator("[data-instant-quote-submit]").IsDisabledAsync());
        Assert.False(await page.Locator("#instant-quotation-form").EvaluateAsync<bool>(
            "form => form.hasAttribute('aria-busy')"));
        await page.Locator(".instant-quote__customer-actions button[type=button]").ClickAsync();
        Assert.Equal("empty", await page.Locator(".instant-quote__workflow").GetAttributeAsync("data-workflow-state"));
        Assert.True(await page.Locator("[data-workflow-upload]").IsVisibleAsync());
        Assert.Equal(0, effects.PreparationCalls);
        Assert.Equal(0, effects.SubmissionCalls);
        Assert.Equal(0, effects.ProfileReads);
        Assert.Equal(0, effects.TokenReads);
        Assert.Equal(0, effects.ExternalRequests);
    }

    /// <summary>Requires the actual preview handlers to restore the previous workflow state without replacing its coordinator.</summary>
    /// <param name="fixture">Whether the server supplied the local-UI display flag.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Preview_OnlyOverlaysPresentationAndBackRestoresPreviousState(bool fixture)
    {
        var component = new InstantQuotationWorkflow();
        ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(InstantQuotationWorkflow.CustomerModel)] = DevelopmentCustomerFixture.ApplyProfile(InstantQuotationCustomerDisplayModel.Empty)
                with { IsDevelopmentCustomerFixture = fixture },
            [nameof(InstantQuotationWorkflow.InitialState)] = InstantQuotationWorkflowState.Uploaded,
        }).SetParameterProperties(component);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var coordinatorField = typeof(InstantQuotationWorkflow).GetField("workflow", flags)!;
        var originalCoordinator = coordinatorField.GetValue(component);
        typeof(InstantQuotationWorkflow).GetMethod("ShowDevelopmentCustomerPreview", flags)!.Invoke(component, null);
        Assert.Equal(fixture ? InstantQuotationWorkflowState.CustomerDetails : InstantQuotationWorkflowState.Uploaded,
            typeof(InstantQuotationWorkflow).GetProperty("State", flags)!.GetValue(component));
        Assert.Same(originalCoordinator, coordinatorField.GetValue(component));
        typeof(InstantQuotationWorkflow).GetMethod("ReturnToReview", flags)!.Invoke(component, null);
        Assert.Equal(InstantQuotationWorkflowState.Uploaded,
            typeof(InstantQuotationWorkflow).GetProperty("State", flags)!.GetValue(component));
        Assert.Same(originalCoordinator, coordinatorField.GetValue(component));
    }
}
