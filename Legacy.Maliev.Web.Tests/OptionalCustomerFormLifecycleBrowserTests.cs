using System.Net;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed class OptionalCustomerFormLifecycleBrowserTests
{
    [Fact]
    public Task RenderedReplacementPreservesLocksAndNormalizesOnlyCurrentForm() => RunAsync(async page =>
    {
        var company = page.Locator("#instant-quote-company");
        var branch = page.Locator("#instant-quote-tax-branch");
        var code = page.Locator("#instant-quote-tax-branch-code");
        var submit = page.Locator("[data-instant-quote-submit]");

        await Assertions.Expect(submit).ToBeEnabledAsync();
        await page.EvaluateAsync("""
            () => {
                const company = document.querySelector('#instant-quote-company');
                company.replaceWith(company.cloneNode(true));
            }
            """);
        await company.FillAsync(" -- ");
        await company.PressAsync("Tab");
        await Assertions.Expect(company).ToHaveValueAsync("");

        await page.EvaluateAsync("""
            () => {
                const oldForm = document.querySelector('form');
                window.retiredForm = oldForm;
                window.originalSubmit = document.querySelector('[data-instant-quote-submit]');
                oldForm.replaceWith(document.querySelector('#locked').content.querySelector('form').cloneNode(true));
            }
            """);
        await Assertions.Expect(branch).ToBeDisabledAsync();
        await Assertions.Expect(branch).ToHaveValueAsync("branch");
        await Assertions.Expect(code).ToHaveAttributeAsync("readonly", "");
        await Assertions.Expect(code).ToHaveValueAsync("00017");
        Assert.True(await submit.EvaluateAsync<bool>("button => button === window.originalSubmit && button.form === document.querySelector('form')"));
        Assert.Equal("branch", await page.EvaluateAsync<string>("() => new FormData(document.querySelector('form')).get('TaxBranch')"));
        Assert.Equal("00017", await page.EvaluateAsync<string>("() => new FormData(document.querySelector('form')).get('TaxBranchCode')"));

        // Let the real button listeners run. Cancel the click default before it can submit.
        await page.EvaluateAsync("""
            () => {
                document.addEventListener('click', e => e.preventDefault(), { once: true });
                window.retiredForm.elements.namedItem('Company').value = '--';
                document.querySelector('#instant-quote-company').value = '--';
                window.originalSubmit.click();
            }
            """);
        await Assertions.Expect(company).ToHaveValueAsync("");
        Assert.Equal("--", await page.EvaluateAsync<string>("() => window.retiredForm.elements.namedItem('Company').value"));

        await page.EvaluateAsync("""
            () => {
                window.taxControl = document.querySelector('#instant-quote-taxnumber');
                window.taxPlaceholder = document.createComment('partial render');
                window.taxControl.replaceWith(window.taxPlaceholder);
            }
            """);
        await Assertions.Expect(page.Locator("#instant-quote-taxnumber")).ToHaveCountAsync(0);
        await Assertions.Expect(code).ToHaveValueAsync("00017");
        await page.EvaluateAsync("() => window.taxPlaceholder.replaceWith(window.taxControl.cloneNode(true))");
        await Assertions.Expect(branch).ToBeDisabledAsync();
        await Assertions.Expect(code).ToHaveValueAsync("00017");
    });

    [Fact]
    public Task SameFormReplacementCannotUnlockBusySubmit() => RunAsync(async page =>
    {
        var submit = page.Locator("[data-instant-quote-submit]");
        var branch = page.Locator("#instant-quote-tax-branch");
        var code = page.Locator("#instant-quote-tax-branch-code");
        await Assertions.Expect(submit).ToBeEnabledAsync();
        var busy = await page.EvaluateAsync<bool[]>("""
            async () => {
                const form = document.querySelector('form');
                const button = document.querySelector('[data-instant-quote-submit]');
                form.setAttribute('aria-busy', 'true');
                button.disabled = true;
                const field = form.elements.namedItem('TaxBranchCode');
                field.replaceWith(field.cloneNode(true));
                // Observe after the route's child-list MutationObserver has run.
                await new Promise(resolve => setTimeout(resolve, 0));
                return [button.disabled, form.querySelector('#instant-quote-tax-branch').disabled,
                    form.elements.namedItem('TaxBranchCode').disabled];
            }
            """);
        Assert.All(busy, state => Assert.True(state));
        await Assertions.Expect(code).ToHaveValueAsync("00008");

        await page.EvaluateAsync("""
            () => {
                const form = document.querySelector('form');
                form.removeAttribute('aria-busy');
                form.elements.namedItem('TaxNumber').dispatchEvent(new Event('input', { bubbles: true }));
            }
            """);
        await Assertions.Expect(submit).ToBeEnabledAsync();
        await Assertions.Expect(branch).ToBeEnabledAsync();
        await Assertions.Expect(code).ToBeEnabledAsync();
        await Assertions.Expect(code).ToHaveValueAsync("00008");
    });

    private static async Task RunAsync(Func<IPage, Task> assertion)
    {
        var model = InstantQuotationCustomerDisplayModel.Empty with
        {
            FirstName = "Synthetic",
            LastName = "Fixture",
            Email = "fixture@example.test",
            Mobile = "0000000000",
            Country = "Thailand",
            Countries = [new("Thailand")],
            BillingStreet1 = "Fixture Street",
            BillingCity = "Fixture District",
            BillingProvince = "Fixture Province",
            BillingPostalCode = "00000",
            TaxNumber = "0000000000000",
            TaxBranch = "branch",
            TaxBranchCode = "00008",
        };
        var initial = await RenderAsync(model);
        var locked = await RenderAsync(model with { TaxBranchCode = "00017", LockedFields = ["TaxBranch", "TaxBranchCode"] });
        const string routeAsset = "/dist/route-instant-quotation.js";
        var assetPath = Path.Combine(BrowserHostIdentityVerifier.SourceProjectDirectory(), "wwwroot", routeAsset.TrimStart('/'));

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var server = builder.Build();
        int hostWrites = 0, browserWrites = 0;
        server.Run(async context =>
        {
            if (!HttpMethods.IsGet(context.Request.Method))
            {
                Interlocked.Increment(ref hostWrites);
                context.Abort();
                return;
            }
            if (context.Request.Path == "/")
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync($"<!doctype html><html><body>{initial}<template id=\"locked\">{locked}</template><script src=\"{routeAsset}\" ></script></body></html>");
            }
            else if (context.Request.Path == routeAsset)
            {
                context.Response.ContentType = "text/javascript";
                await context.Response.SendFileAsync(assetPath);
            }
            else context.Response.StatusCode = 404;
        });
        await server.StartAsync();
        var origin = new Uri(Assert.Single(server.Urls));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block });
        await context.RouteAsync("**/*", async route =>
        {
            var url = new Uri(route.Request.Url);
            if (route.Request.Method == "GET" && url.GetLeftPart(UriPartial.Authority) == origin.GetLeftPart(UriPartial.Authority)
                && url.AbsolutePath is "/" or routeAsset)
            {
                await route.ContinueAsync();
                return;
            }
            if (route.Request.Method == "POST") Interlocked.Increment(ref browserWrites);
            await route.AbortAsync();
        });
        await context.RouteWebSocketAsync("**/*", route => _ = route.CloseAsync());
        await using var page = await context.NewPageAsync();
        page.SetDefaultTimeout(10_000);
        var response = await page.GotoAsync(origin.AbsoluteUri);
        Assert.Equal(200, response?.Status);
        await assertion(page);
        Assert.Equal(0, browserWrites);
        Assert.Equal(0, hostWrites);
    }

    private static async Task<string> RenderAsync(InstantQuotationCustomerDisplayModel model)
    {
        using var services = new ServiceCollection().AddLogging()
            .AddLocalization()
            .AddSingleton<NavigationManager>(new InstantQuotationTestNavigationManager())
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<InstantQuotationCustomerForm>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(InstantQuotationCustomerForm.Model)] = model }));
            return rendered.ToHtmlString();
        });
    }
}
