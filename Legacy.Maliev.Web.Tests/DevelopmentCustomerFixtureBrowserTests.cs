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
        await Assertions.Expect(page.Locator(".instant-quote__workflow")).ToHaveAttributeAsync("data-workflow-state", "empty");
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
                with
            { IsDevelopmentCustomerFixture = fixture },
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

    [Theory]
    [InlineData("disposed")]
    [InlineData("initializationFailed")]
    [InlineData("batchInProgress")]
    [InlineData("bulkApplying")]
    public void Preview_RefusesWhenTheExistingWorkflowIsBlocked(string blockedField)
    {
        var component = new InstantQuotationWorkflow();
        ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(InstantQuotationWorkflow.CustomerModel)] = DevelopmentCustomerFixture.ApplyProfile(InstantQuotationCustomerDisplayModel.Empty),
            [nameof(InstantQuotationWorkflow.InitialState)] = InstantQuotationWorkflowState.Uploaded,
        }).SetParameterProperties(component);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(InstantQuotationWorkflow).GetField(blockedField, flags)!.SetValue(component, true);

        typeof(InstantQuotationWorkflow).GetMethod("ShowDevelopmentCustomerPreview", flags)!.Invoke(component, null);

        Assert.False((bool)typeof(InstantQuotationWorkflow).GetField("developmentCustomerPreview", flags)!.GetValue(component)!);
        var expected = blockedField == "initializationFailed"
            ? InstantQuotationWorkflowState.Error : InstantQuotationWorkflowState.Uploaded;
        Assert.Equal(expected, typeof(InstantQuotationWorkflow).GetProperty("State", flags)!.GetValue(component));
    }

    [Fact]
    public void Preview_DoesNotHideAnErrorThatAppearsAfterItOpens()
    {
        var component = new InstantQuotationWorkflow();
        ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(InstantQuotationWorkflow.CustomerModel)] = DevelopmentCustomerFixture.ApplyProfile(InstantQuotationCustomerDisplayModel.Empty),
            [nameof(InstantQuotationWorkflow.InitialState)] = InstantQuotationWorkflowState.Uploaded,
        }).SetParameterProperties(component);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(InstantQuotationWorkflow).GetMethod("ShowDevelopmentCustomerPreview", flags)!.Invoke(component, null);
        Assert.Equal(InstantQuotationWorkflowState.CustomerDetails, typeof(InstantQuotationWorkflow).GetProperty("State", flags)!.GetValue(component));

        typeof(InstantQuotationWorkflow).GetField("initializationFailed", flags)!.SetValue(component, true);

        Assert.Equal(InstantQuotationWorkflowState.Error, typeof(InstantQuotationWorkflow).GetProperty("State", flags)!.GetValue(component));
    }

    [Theory]
    [InlineData(InstantQuotationWorkflowState.Error)]
    [InlineData(InstantQuotationWorkflowState.Uploading)]
    [InlineData(InstantQuotationWorkflowState.Submitted)]
    public void Preview_PreservesUnderlyingErrorBusyAndSubmittedStates(InstantQuotationWorkflowState state)
    {
        var component = new InstantQuotationWorkflow();
        ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(InstantQuotationWorkflow.CustomerModel)] = DevelopmentCustomerFixture.ApplyProfile(InstantQuotationCustomerDisplayModel.Empty),
            [nameof(InstantQuotationWorkflow.InitialState)] = state,
        }).SetParameterProperties(component);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(InstantQuotationWorkflow).GetMethod("ShowDevelopmentCustomerPreview", flags)!.Invoke(component, null);
        Assert.False((bool)typeof(InstantQuotationWorkflow).GetField("developmentCustomerPreview", flags)!.GetValue(component)!);
        Assert.Equal(state, typeof(InstantQuotationWorkflow).GetProperty("State", flags)!.GetValue(component));
    }

    [Fact]
    public void Preview_DoesNotBypassFailedLocalSelection()
    {
        var component = new InstantQuotationWorkflow();
        ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(InstantQuotationWorkflow.CustomerModel)] = DevelopmentCustomerFixture.ApplyProfile(InstantQuotationCustomerDisplayModel.Empty),
            [nameof(InstantQuotationWorkflow.InitialState)] = InstantQuotationWorkflowState.Uploaded,
        }).SetParameterProperties(component);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var failureType = typeof(InstantQuotationWorkflow).GetNestedType("LocalPreviewFailure", flags)!;
        var failure = Activator.CreateInstance(failureType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null, args: ["selected.stl", "validation"], culture: null)!;
        var failures = (System.Collections.IDictionary)typeof(InstantQuotationWorkflow)
            .GetField("failedPreviewSelections", flags)!.GetValue(component)!;
        failures.Add("selection", failure);

        typeof(InstantQuotationWorkflow).GetMethod("ShowDevelopmentCustomerPreview", flags)!.Invoke(component, null);

        Assert.False((bool)typeof(InstantQuotationWorkflow).GetField("developmentCustomerPreview", flags)!.GetValue(component)!);
        Assert.Single(failures.Keys.Cast<object>());
        Assert.Equal(InstantQuotationWorkflowState.Uploaded, typeof(InstantQuotationWorkflow).GetProperty("State", flags)!.GetValue(component));
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("batchInProgress")]
    [InlineData("bulkApplying")]
    public void OpenPreview_YieldsWhenWorkflowBecomesUnavailable(string blockedField)
    {
        var component = new InstantQuotationWorkflow();
        ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(InstantQuotationWorkflow.CustomerModel)] = DevelopmentCustomerFixture.ApplyProfile(InstantQuotationCustomerDisplayModel.Empty),
            [nameof(InstantQuotationWorkflow.InitialState)] = InstantQuotationWorkflowState.Uploaded,
        }).SetParameterProperties(component);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(InstantQuotationWorkflow).GetMethod("ShowDevelopmentCustomerPreview", flags)!.Invoke(component, null);
        Assert.Equal(InstantQuotationWorkflowState.CustomerDetails, typeof(InstantQuotationWorkflow).GetProperty("State", flags)!.GetValue(component));

        typeof(InstantQuotationWorkflow).GetField(blockedField, flags)!.SetValue(component, true);

        Assert.Equal(InstantQuotationWorkflowState.Uploaded, typeof(InstantQuotationWorkflow).GetProperty("State", flags)!.GetValue(component));
    }

    [Fact]
    public void OpenPreview_YieldsToRepricingAndFailedLocalSelection()
    {
        var component = new InstantQuotationWorkflow();
        ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(InstantQuotationWorkflow.CustomerModel)] = DevelopmentCustomerFixture.ApplyProfile(InstantQuotationCustomerDisplayModel.Empty),
            [nameof(InstantQuotationWorkflow.InitialState)] = InstantQuotationWorkflowState.Uploaded,
        }).SetParameterProperties(component);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(InstantQuotationWorkflow).GetMethod("ShowDevelopmentCustomerPreview", flags)!.Invoke(component, null);
        Assert.Equal(InstantQuotationWorkflowState.CustomerDetails, typeof(InstantQuotationWorkflow).GetProperty("State", flags)!.GetValue(component));
        var repricing = typeof(InstantQuotationWorkflow).GetField("repricing", flags)!.GetValue(component)!;
        var active = repricing.GetType().GetField("active", flags)!;

        active.SetValue(repricing, 1);
        Assert.Equal(InstantQuotationWorkflowState.Uploaded, typeof(InstantQuotationWorkflow).GetProperty("State", flags)!.GetValue(component));
        active.SetValue(repricing, 0);
        var failureType = typeof(InstantQuotationWorkflow).GetNestedType("LocalPreviewFailure", flags)!;
        var failure = Activator.CreateInstance(failureType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null, args: ["selected.stl", "validation"], culture: null)!;
        var failures = (System.Collections.IDictionary)typeof(InstantQuotationWorkflow)
            .GetField("failedPreviewSelections", flags)!.GetValue(component)!;
        failures.Add("selection", failure);
        Assert.Equal(InstantQuotationWorkflowState.Uploaded, typeof(InstantQuotationWorkflow).GetProperty("State", flags)!.GetValue(component));
        Assert.Single(failures.Keys.Cast<object>());
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("incomplete")]
    [InlineData("failure")]
    public void OpenPreview_YieldsToLocalFileWorkAndReturnsWhenItClears(string localState)
    {
        var component = new InstantQuotationWorkflow();
        ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(InstantQuotationWorkflow.CustomerModel)] = DevelopmentCustomerFixture.ApplyProfile(InstantQuotationCustomerDisplayModel.Empty),
            [nameof(InstantQuotationWorkflow.InitialState)] = InstantQuotationWorkflowState.Uploaded,
        }).SetParameterProperties(component);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(InstantQuotationWorkflow);
        var state = type.GetProperty("State", flags)!;
        var sections = type.GetProperty("VisibleSections", flags)!;
        type.GetMethod("ShowDevelopmentCustomerPreview", flags)!.Invoke(component, null);
        Assert.Equal(InstantQuotationWorkflowState.CustomerDetails, state.GetValue(component));
        Assert.True((bool)sections.GetValue(component)!.GetType().GetProperty("CustomerDetails")!.GetValue(sections.GetValue(component))!);

        var field = localState == "pending" ? "pendingPreviewSelections" : "failedPreviewSelections";
        var items = (System.Collections.IDictionary)type.GetField(field, flags)!.GetValue(component)!;
        if (localState == "incomplete") type.GetField("incompletePreviewKey", flags)!.SetValue(component, "selection");
        else
        {
            var nested = type.GetNestedType(localState == "pending" ? "PendingLocalPreview" : "LocalPreviewFailure", flags)!;
            var args = localState == "pending" ? new object[] { "selected.stl", 1L } : new object[] { "selected.stl", "validation" };
            items.Add("selection", Activator.CreateInstance(nested,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                binder: null, args: args, culture: null)!);
        }

        Assert.Equal(InstantQuotationWorkflowState.Uploaded, state.GetValue(component));
        var visible = sections.GetValue(component)!;
        Assert.False((bool)visible.GetType().GetProperty("CustomerDetails")!.GetValue(visible)!);
        Assert.True((bool)visible.GetType().GetProperty("Viewer")!.GetValue(visible)!);

        items.Clear();
        type.GetField("incompletePreviewKey", flags)!.SetValue(component, null);
        Assert.Equal(InstantQuotationWorkflowState.CustomerDetails, state.GetValue(component));
        visible = sections.GetValue(component)!;
        Assert.True((bool)visible.GetType().GetProperty("CustomerDetails")!.GetValue(visible)!);
    }
}
