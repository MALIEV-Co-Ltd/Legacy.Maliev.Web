using System.Diagnostics;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationCustomerLayoutBrowserTests(CncNativeBrowserFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(320, 700, "en", ColorScheme.Light)]
    [InlineData(320, 700, "en", ColorScheme.Dark)]
    [InlineData(320, 700, "th", ColorScheme.Light)]
    [InlineData(320, 700, "th", ColorScheme.Dark)]
    [InlineData(375, 667, "en", ColorScheme.Light)]
    [InlineData(375, 667, "en", ColorScheme.Dark)]
    [InlineData(375, 667, "th", ColorScheme.Light)]
    [InlineData(375, 667, "th", ColorScheme.Dark)]
    [InlineData(820, 800, "en", ColorScheme.Light)]
    [InlineData(820, 800, "en", ColorScheme.Dark)]
    [InlineData(820, 800, "th", ColorScheme.Light)]
    [InlineData(820, 800, "th", ColorScheme.Dark)]
    [InlineData(1280, 800, "en", ColorScheme.Light)]
    [InlineData(1280, 800, "en", ColorScheme.Dark)]
    [InlineData(1280, 800, "th", ColorScheme.Light)]
    [InlineData(1280, 800, "th", ColorScheme.Dark)]
    public async Task CustomerLedgerAndFormRemainReadableAtRepresentativeWidths(int width, int height, string culture, ColorScheme colorScheme)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            ColorScheme = colorScheme,
            HasTouch = width <= 375,
        });
        await using var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync(new Uri(new Uri(origin), $"/instantquotation/3d-printing?culture={culture}").ToString());
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        // The synthetic customer step must not be replaced by Blazor's first interactive render.
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId !== undefined");

        await page.EvaluateAsync("""
            () => {
              const workflow = document.querySelector('.instant-quote__workflow');
              workflow.dataset.workflowState = 'customerdetails';
              workflow.innerHTML = `
                <section data-workflow-viewer><canvas></canvas></section>
                <aside class="instant-quote__customer-order" data-workflow-customer-order>
                  <header><h2>Parts</h2></header>
                  <div class="instant-quote__customer-order-scroll"><table>
                    <thead><tr><th>Part</th><th>Quantity</th><th>Subtotal</th></tr></thead>
                    <tbody>${Array.from({length: 12}, (_, index) => `<tr><td><span class="instant-quote__customer-order-part"><img alt="" src="/src/images/3d-canvas-placeholder.svg"><span class="instant-quote__customer-order-identity"><strong>Long customer part name ${index}</strong><small>Material</small></span></span></td><td>12</td><td>฿12,345.67</td></tr>`).join('')}</tbody>
                  </table></div>
                  <section class="instant-quote__pricing-summary"><h3>Order summary</h3><dl><div><dt>Shipping</dt><dd>฿100.00</dd></div><div><dt>VAT</dt><dd>฿864.00</dd></div><div><dt>Total</dt><dd>฿12,964.00</dd></div></dl></section>
                </aside>
                <div data-workflow-customer-details><section data-workflow-customer-details-content>
                  <header class="instant-quote__panel-header"><h3>Customer details</h3></header>
                  <span class="instant-quote__flow-step">Step 3 of 3</span>
                  <form id="instant-quotation-form">${Array.from({length: 16}, (_, index) => `<div><label for="field-${index}">Customer field ${index}</label><input id="field-${index}" value="A sample value"></div>`).join('')}</form>
                  <div class="instant-quote__actions instant-quote__customer-actions"><p role="note">Our team will review your files, then send an official quotation with payment instructions.</p><button type="button">Back</button><button type="submit" form="instant-quotation-form">Submit</button></div>
                </section></div>`;
              document.querySelector('#instant-quotation-form').addEventListener('submit', event => {
                event.preventDefault();
                event.currentTarget.dataset.submitted = 'true';
              });
            }
            """);

        Assert.False(await page.Locator("[data-workflow-viewer]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-workflow-customer-order]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-workflow-customer-details]").IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        Assert.True(await page.Locator(".instant-quote__customer-actions button[type=submit]").EvaluateAsync<bool>(
            "button => button.form?.id === 'instant-quotation-form' && button.getBoundingClientRect().height >= 44"));
        Assert.True(await page.Locator(".instant-quote__customer-order").EvaluateAsync<bool>(
            "element => { const box = element.getBoundingClientRect(); return box.left >= -1 && box.right <= innerWidth + 1; }"));

        if (width >= 1024)
        {
            Assert.True(await page.EvaluateAsync<bool>(
                "() => document.querySelector('.instant-quote__customer-order').getBoundingClientRect().right <= document.querySelector('[data-workflow-customer-details]').getBoundingClientRect().left + 1"));
            Assert.True(await page.Locator("[data-workflow-customer-details-content] form").EvaluateAsync<bool>(
                "element => element.scrollHeight > element.clientHeight"));
            Assert.True(await page.Locator(".instant-quote__customer-actions").EvaluateAsync<bool>(
                "footer => { const pane = footer.closest('[data-workflow-customer-details]').getBoundingClientRect(); const action = footer.getBoundingClientRect(); return action.top >= pane.top && action.bottom <= pane.bottom + 1; }"));
            await page.Locator("#field-15").ScrollIntoViewIfNeededAsync();
            Assert.True(await page.Locator(".instant-quote__customer-actions button[type=submit]").IsVisibleAsync());
            Assert.True(await page.Locator(".instant-quote__customer-order-scroll").EvaluateAsync<bool>(
                "element => element.scrollHeight > element.clientHeight"));
            var lastPart = page.Locator(".instant-quote__customer-order-scroll tbody tr").Last;
            await lastPart.ScrollIntoViewIfNeededAsync();
            Assert.True(await lastPart.EvaluateAsync<bool>("""
                element => {
                  const row = element.getBoundingClientRect();
                  const rail = element.closest('.instant-quote__customer-order-scroll').getBoundingClientRect();
                  return row.top >= rail.top - 1 && row.bottom <= rail.bottom + 1;
                }
                """));
            Assert.True(await page.Locator(".instant-quote__pricing-summary").IsVisibleAsync());
        }
        else
        {
            Assert.True(await page.EvaluateAsync<bool>(
                "() => document.querySelector('.instant-quote__customer-order').getBoundingClientRect().bottom <= document.querySelector('[data-workflow-customer-details]').getBoundingClientRect().top + 1"));
        }
        await page.Locator(".instant-quote__customer-actions button[type=submit]").ClickAsync();
        Assert.Equal("true", await page.Locator("#instant-quotation-form").GetAttributeAsync("data-submitted"));
        if (Environment.GetEnvironmentVariable("MALIEV_WEB_VALIDATION_SCREENSHOTS") == "1"
            && width is 320 or 1280 && culture == "th" && colorScheme == ColorScheme.Light)
        {
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(Path.GetTempPath(), $"legacy-web-276-customer-{culture}-{width}.png"),
            });
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task RealCustomerFormPreservesStructuredSectionsCounterAndResponsiveOrder(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
        });
        var probe = new SubmitBoundaryProbe();
        await using var application = new SubmitBoundaryFactory(probe);
        application.UseKestrel(0);
        application.ClientOptions.AllowAutoRedirect = false;
        using var hostClient = application.CreateClient();
        var origin = hostClient.BaseAddress!.GetLeftPart(UriPartial.Authority);
        probe.Origin = new Uri(origin);
        using var routingSubscription = application.Services.GetRequiredService<DiagnosticListener>()
            .Subscribe(probe, name => name == "Microsoft.AspNetCore.Routing.EndpointMatched");
        probe.ObserveRoutes(application.Services.GetRequiredService<EndpointDataSource>());
        using (var directPost = new HttpRequestMessage(HttpMethod.Post, "/InstantQuotation/3D-Printing?handler=SubmitRequest"))
        {
            directPost.Headers.Accept.ParseAdd("text/html");
            directPost.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Country"] = "Thailand" });
            using var directResponse = await hostClient.SendAsync(directPost);
            output.WriteLine($"[customer-layout-direct-post] status={(int)directResponse.StatusCode}; razorEndpoint={probe.RazorEndpoint}; allowsPost={probe.AllowsPost}; methodRejection={probe.MethodRejection}; secureTransport={probe.SecureTransport}; antiforgeryCookie={probe.AntiforgeryCookie}; routingSnapshots={probe.RoutingSnapshots}");
        }
        await context.AddCookiesAsync(
        [
            new Cookie { Name = ".AspNetCore.Culture", Value = $"c={culture}|uic={culture}", Url = origin },
        ]);
        await using var page = await context.NewPageAsync();
        var submissions = 0;
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.Contains("handler=SubmitRequest", StringComparison.Ordinal))
                Interlocked.Increment(ref submissions);
        };
        await page.GotoAsync(new Uri(new Uri(origin), $"/instantquotation/3d-printing?culture={culture}").ToString());
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        // Read a normal antiforgery form without navigating to or executing the login page.
        // Missing required customer fields take the existing validation redirect before
        // the submission service, creating the real customer-details child, not test HTML.
        var submitResponse = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && Uri.TryCreate(response.Url, UriKind.Absolute, out var responseUri)
            && responseUri.GetLeftPart(UriPartial.Authority) == origin
            && string.Equals(responseUri.AbsolutePath, "/InstantQuotation/3D-Printing", StringComparison.OrdinalIgnoreCase)
            && responseUri.Query == "?handler=SubmitRequest",
            new() { Timeout = 30000 });
        async Task ObserveSubmitResponseAsync()
        {
            var response = await submitResponse;
            var location = await response.HeaderValueAsync("location");
            var redirectsToQuotation = Uri.TryCreate(new Uri(origin), location, out var redirect)
                && redirect.GetLeftPart(UriPartial.Authority) == origin
                && string.Equals(redirect.AbsolutePath, "/InstantQuotation/3D-Printing", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(redirect.Fragment);
            try
            {
                output.WriteLine($"[customer-layout-submit] status={response.Status}; redirectsToQuotation={redirectsToQuotation}; routes={probe.PrintingRoutes}; postRoutes={probe.PrintingPostRoutes}; razorPostRoutes={probe.PrintingRazorPostRoutes}; routeDetails={probe.PrintingRouteDetails}; observed={probe.Observed}; originalStatus={probe.OriginalStatus}; reExecuted={probe.ReExecuted}; razorEndpoint={probe.RazorEndpoint}; allowsPost={probe.AllowsPost}; methodRejection={probe.MethodRejection}; secureTransport={probe.SecureTransport}; antiforgeryCookie={probe.AntiforgeryCookie}; antiforgery={probe.Antiforgery}; observerFailed={probe.ObserverFailed}; routingSnapshots={probe.RoutingSnapshots}");
            }
            catch { /* Secondary diagnostic output must not replace the original test failure. */ }
            Assert.Equal(302, response.Status);
            Assert.True(redirectsToQuotation);
        }

        await Task.WhenAll(
            page.WaitForURLAsync(new Uri(new Uri(origin), "/InstantQuotation/3D-Printing" + (culture == "en" ? "?culture=en" : string.Empty)).ToString(), new() { Timeout = 30000 }),
            page.EvaluateAsync("""
                async () => {
                  const response = await fetch('/account/login', { credentials: 'same-origin' });
                  if (!response.ok) throw new Error('The antiforgery form was not available.');
                  const documentSource = new DOMParser().parseFromString(await response.text(), 'text/html');
                  const token = documentSource.querySelector('input[name="__RequestVerificationToken"]');
                  if (!token?.value) throw new Error('The antiforgery field was not available.');
                  const form = document.createElement('form');
                  form.method = 'post';
                  form.action = '/InstantQuotation/3D-Printing?handler=SubmitRequest';
                  for (const [name, value] of [[token.name, token.value], ['Country', 'Thailand']]) {
                    const input = document.createElement('input');
                    input.type = 'hidden'; input.name = name; input.value = value; form.appendChild(input);
                  }
                  document.body.appendChild(form);
                  HTMLFormElement.prototype.submit.call(form);
                }
                """),
            ObserveSubmitResponseAsync());
        var content = page.Locator("[data-workflow-customer-details-content]");
        await Assertions.Expect(content).ToBeVisibleAsync();
        var form = content.Locator("#instant-quotation-form");
        await Assertions.Expect(form.Locator(".instant-quote__customer-section")).ToHaveCountAsync(3);
        foreach (var heading in culture == "th"
            ? new[] { "ข้อมูลผู้ติดต่อหลัก", "บริษัทและข้อมูลออกใบกำกับภาษี", "รายละเอียดโครงการ" }
            : new[] { "Primary contact", "Company & billing", "Project details" })
        {
            await Assertions.Expect(form.Locator(".instant-quote__customer-section h4").Filter(new() { HasText = heading })).ToHaveCountAsync(1);
        }
        await Assertions.Expect(form.Locator(".instant-quote__customer-progress")).ToHaveCountAsync(0);
        await Assertions.Expect(form.Locator("input[name='FirstName']")).ToHaveAttributeAsync("placeholder", culture == "th" ? "กรอกชื่อ" : "Enter your first name");
        await Assertions.Expect(form.Locator("input[name='LastName']")).ToHaveAttributeAsync("placeholder", culture == "th" ? "กรอกนามสกุล" : "Enter your last name");
        await Assertions.Expect(form.Locator("input[name='Company']")).ToHaveAttributeAsync("placeholder", culture == "th" ? "กรอกชื่อบริษัท" : "Enter your company name");
        await Assertions.Expect(form.Locator("input[name='Email']")).ToHaveAttributeAsync("placeholder", "name@company.com");
        await Assertions.Expect(form.Locator("input[name='Telephone']")).ToHaveAttributeAsync("placeholder", "e.g. +66 2 123 4567");
        await Assertions.Expect(form.Locator("input[name='TaxNumber']")).ToHaveAttributeAsync("placeholder", "e.g. 0105559123456");
        await Assertions.Expect(form).ToHaveAttributeAsync("method", "post");
        await Assertions.Expect(form).ToHaveAttributeAsync("action", "/InstantQuotation/3D-Printing?handler=SubmitRequest");
        Assert.Equal(1, await form.Locator("input[name='__RequestVerificationToken']").CountAsync());
        Assert.True(await form.EvaluateAsync<bool>("form => !form.checkValidity()"));
        Assert.True(await form.EvaluateAsync<bool>("""
            form => [...form.querySelectorAll('input[name]:not([type=hidden]), select[name], textarea[name]')]
              .every(field => field.labels?.length > 0)
            """));
        var names = await form.Locator("input[name]:not([type=hidden]), select[name], textarea[name]")
            .EvaluateAllAsync<string[]>("fields => fields.map(field => field.name)");
        var description = form.Locator("textarea[name='Description']");
        await Assertions.Expect(description).ToHaveAttributeAsync("maxlength", "512");
        var counter = form.Locator("[data-customer-description-count]");
        await Assertions.Expect(counter).ToHaveTextAsync("0");
        await description.FillAsync(culture == "th" ? "งานทดสอบ" : "Test project");
        await Assertions.Expect(counter).ToHaveTextAsync(culture == "th" ? "8" : "12");
        await description.FillAsync(new string('x', 512));
        await Assertions.Expect(counter).ToHaveTextAsync("512");
        Assert.Equal(512, await form.EvaluateAsync<int>("form => new FormData(form).get('Description').length"));

        var desktop = await form.EvaluateAsync<System.Text.Json.JsonElement>("""
            form => ({
              workspaceColumns:getComputedStyle(form).gridTemplateColumns.trim().split(/\s+/).length,
              fieldColumns:getComputedStyle(form.querySelector('.instant-quote__customer-fields')).gridTemplateColumns.trim().split(/\s+/).length,
              progressRemoved:form.querySelector('.instant-quote__customer-progress') === null
            })
            """);
        Assert.Equal(1, desktop.GetProperty("workspaceColumns").GetInt32());
        Assert.Equal(2, desktop.GetProperty("fieldColumns").GetInt32());
        Assert.True(desktop.GetProperty("progressRemoved").GetBoolean());
        Assert.True(await page.Locator(".instant-quote__customer-actions").EvaluateAsync<bool>("""
            footer => {
              const pane=footer.closest('[data-workflow-customer-details]').getBoundingClientRect();
              const action=footer.getBoundingClientRect();
              return action.top>=pane.top-1 && action.bottom<=pane.bottom+1;
            }
            """));
        Assert.True(await page.Locator("[data-workflow-customer-order] .instant-quote__pricing-summary").IsVisibleAsync());

        foreach (var width in new[] { 820, 390 })
        {
            await page.SetViewportSizeAsync(width, 844);
            await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
            var geometry = await form.EvaluateAsync<System.Text.Json.JsonElement>("""
                form => ({
                  workspaceColumns:getComputedStyle(form).gridTemplateColumns.trim().split(/\s+/).length,
                  fieldColumns:getComputedStyle(form.querySelector('.instant-quote__customer-fields')).gridTemplateColumns.trim().split(/\s+/).length,
                  progressRemoved:form.querySelector('.instant-quote__customer-progress') === null,
                  formFits:form.scrollWidth<=form.clientWidth+1,
                  documentFits:document.documentElement.scrollWidth<=innerWidth+1,
                  controls:[...form.querySelectorAll('input:not([type=hidden]):not([type=checkbox]), select, textarea')]
                    .filter(field => field.getClientRects().length > 0)
                    .every(field => { const box=field.getBoundingClientRect(); return box.width>=44 && box.height>=44; })
                })
                """);
            Assert.Equal(1, geometry.GetProperty("workspaceColumns").GetInt32());
            Assert.Equal(width == 390 ? 1 : 2, geometry.GetProperty("fieldColumns").GetInt32());
            Assert.True(geometry.GetProperty("progressRemoved").GetBoolean());
            Assert.True(geometry.GetProperty("formFits").GetBoolean());
            Assert.True(geometry.GetProperty("documentFits").GetBoolean());
            Assert.True(geometry.GetProperty("controls").GetBoolean());
            Assert.Equal(names, await form.Locator("input[name]:not([type=hidden]), select[name], textarea[name]")
                .EvaluateAllAsync<string[]>("fields => fields.map(field => field.name)"));
            await Assertions.Expect(counter).ToHaveTextAsync("512");
        }
        Assert.Equal(1, Volatile.Read(ref submissions));
        Assert.False(await page.Locator("[data-workflow-submitted]").IsVisibleAsync());
    }

    private sealed class SubmitBoundaryFactory(SubmitBoundaryProbe probe)
        : TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory())
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(probe));
        }
    }

    private sealed class SubmitBoundaryProbe : IStartupFilter, IObserver<KeyValuePair<string, object?>>
    {
        private readonly object routingSnapshotKey = new();
        internal Uri? Origin { get; set; }
        internal string RoutingSnapshots { get; private set; } = string.Empty;
        internal int PrintingRoutes { get; private set; }
        internal int PrintingPostRoutes { get; private set; }
        internal int PrintingRazorPostRoutes { get; private set; }
        internal string PrintingRouteDetails { get; private set; } = string.Empty;
        internal bool Observed { get; private set; }
        internal int OriginalStatus { get; private set; }
        internal bool ReExecuted { get; private set; }
        internal bool RazorEndpoint { get; private set; }
        internal bool AllowsPost { get; private set; }
        internal bool MethodRejection { get; private set; }
        internal bool SecureTransport { get; private set; }
        internal bool AntiforgeryCookie { get; private set; }
        internal int Antiforgery { get; private set; }
        internal bool ObserverFailed { get; private set; }

        public void OnNext(KeyValuePair<string, object?> notification)
        {
            if (notification.Key != "Microsoft.AspNetCore.Routing.EndpointMatched"
                || notification.Value is not HttpContext context
                || !IsOwnedHost(context)
                || !context.Items.TryGetValue(routingSnapshotKey, out var value)
                || value is not int count || count >= 2)
            {
                return;
            }

            context.Items[routingSnapshotKey] = count + 1;
            try
            {
                var reExecute = context.Features.Get<IStatusCodeReExecuteFeature>();
                var endpoint = context.GetEndpoint();
                var original = reExecute?.Endpoint;
                var antiforgery = context.Features.Get<IAntiforgeryValidationFeature>();
                RoutingSnapshots += $"[match={count + 1}; errorPath={context.Request.Path.Equals("/Error", StringComparison.OrdinalIgnoreCase)}; razor={endpoint is not null && IsPrintingPage(endpoint)}; allowsPost={endpoint is RouteEndpoint && AcceptsPost(endpoint)}; methodRejection={IsMethodRejection(endpoint)}; reExecuted={reExecute is not null}; originalStatus={reExecute?.OriginalStatusCode ?? 0}; originalRazor={original is not null && IsPrintingPage(original)}; originalAllowsPost={original is RouteEndpoint && AcceptsPost(original)}; originalMethodRejection={IsMethodRejection(original)}; antiforgery={(antiforgery is null ? 0 : antiforgery.IsValid ? 1 : 2)}]";
            }
            catch
            {
                ObserverFailed = true;
            }
        }

        public void OnCompleted() { }

        public void OnError(Exception error) => ObserverFailed = true;

        private bool IsOwnedHost(HttpContext context) => Origin is not null
            && string.Equals(context.Request.Host.Value, Origin.Authority, StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.Request.Scheme, Origin.Scheme, StringComparison.OrdinalIgnoreCase);

        private static bool IsMethodRejection(Endpoint? endpoint) => string.Equals(endpoint?.DisplayName,
            "405 HTTP Method Not Supported", StringComparison.Ordinal);

        internal void ObserveRoutes(EndpointDataSource source)
        {
            foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>().Where(endpoint =>
                string.Equals(endpoint.RoutePattern.RawText?.Trim('/'),
                    "InstantQuotation/3D-Printing", StringComparison.OrdinalIgnoreCase)))
            {
                PrintingRoutes++;
                var methodMetadata = endpoint.Metadata.GetOrderedMetadata<IHttpMethodMetadata>();
                var methods = string.Join("/", methodMetadata.Select(metadata =>
                    string.Join(",", metadata.HttpMethods.Select(method => method is "GET" or "HEAD" or "POST"
                        ? method : "other"))));
                PrintingRouteDetails += $"[pattern={endpoint.RoutePattern.RawText}; order={endpoint.Order}; razor={IsPrintingPage(endpoint)}; suppressed={endpoint.Metadata.GetMetadata<ISuppressMatchingMetadata>()?.SuppressMatching == true}; dynamic={endpoint.Metadata.GetMetadata<IDynamicEndpointMetadata>()?.IsDynamic == true}; methods={methods}]";
                if (!AcceptsPost(endpoint)) continue;
                PrintingPostRoutes++;
                if (IsPrintingPage(endpoint)) PrintingRazorPostRoutes++;
            }
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
        {
            application.Use(async (context, requestNext) =>
            {
                if (IsOwnedHost(context) && HttpMethods.IsPost(context.Request.Method)
                    && context.Request.Path.Equals("/InstantQuotation/3D-Printing", StringComparison.OrdinalIgnoreCase)
                    && context.Request.Query["handler"] == "SubmitRequest")
                {
                    RoutingSnapshots = string.Empty;
                    context.Items[routingSnapshotKey] = 0;
                    context.Response.OnStarting(() =>
                    {
                        try
                        {
                            var reExecute = context.Features.Get<IStatusCodeReExecuteFeature>();
                            var endpoint = reExecute?.Endpoint ?? context.GetEndpoint();
                            var antiforgery = context.Features.Get<IAntiforgeryValidationFeature>();
                            OriginalStatus = reExecute?.OriginalStatusCode ?? context.Response.StatusCode;
                            ReExecuted = reExecute is not null;
                            RazorEndpoint = endpoint is not null && IsPrintingPage(endpoint);
                            AllowsPost = endpoint is RouteEndpoint && AcceptsPost(endpoint);
                            MethodRejection = string.Equals(endpoint?.DisplayName,
                                "405 HTTP Method Not Supported", StringComparison.Ordinal);
                            SecureTransport = context.Request.IsHttps;
                            AntiforgeryCookie = context.Request.Cookies.Keys.Any(name =>
                                name.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
                            Antiforgery = antiforgery is null ? 0 : antiforgery.IsValid ? 1 : 2;
                            Observed = true;
                        }
                        catch
                        {
                            ObserverFailed = true;
                        }
                        return Task.CompletedTask;
                    });
                }
                await requestNext(context);
            });
            next(application);
        };

        private static bool AcceptsPost(Endpoint endpoint) =>
            endpoint.Metadata.GetMetadata<IHttpMethodMetadata>() is not { HttpMethods.Count: > 0 } methods
            || methods.HttpMethods.Contains(HttpMethods.Post, StringComparer.OrdinalIgnoreCase);

        private static bool IsPrintingPage(Endpoint endpoint) =>
            string.Equals(endpoint.Metadata.GetMetadata<PageActionDescriptor>()?.ViewEnginePath,
                "/InstantQuotation/3D-Printing", StringComparison.OrdinalIgnoreCase);
    }
}
