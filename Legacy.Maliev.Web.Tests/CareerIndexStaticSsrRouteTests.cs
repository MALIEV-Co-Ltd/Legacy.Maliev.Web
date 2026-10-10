using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CareerIndexStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public CareerIndexStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Host_DeclaresTheCareerRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Career", "CareerIndexPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Career", "CareerIndexContent.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Career", "Index.cshtml"));

        Assert.Contains("BlazorRouting:CareerIndex", program, StringComparison.Ordinal);
        Assert.Contains("\"/Career/Index\"", program, StringComparison.Ordinal);
        Assert.Contains("\"CareerIndex\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"HideLocalAspireFixture\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(CareerIndexContent)\"", fallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "en",
        "Career | MALIEV",
        "Explore MALIEV career opportunities in engineering, manufacturing, customer support, and project delivery.",
        "Job Offers")]
    [InlineData(
        "th",
        "ตำแหน่งงาน | MALIEV",
        "ดูตำแหน่งงานกับ MALIEV และเรียนรู้วิธีสมัครงานด้านวิศวกรรม การผลิต และการสนับสนุนลูกค้า",
        "ตำแหน่งงาน")]
    public async Task CareerRoute_RendersLocalizedServiceBackedStaticSsrWithSeoAndAnalytics(
        string culture,
        string title,
        string description,
        string heading)
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync(
            $"/career?culture={culture}&sort=JobId_Ascending&search=%20engineer%20&index=3&size=50&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ร่วมสร้างงานผลิตที่ใช้งานได้จริงกับ MALIEV" : "Build useful manufacturing work with MALIEV", source, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "วิศวกรรมและการผลิต" : "Engineering and production", source, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ดูแลลูกค้าและโปรเจ็ค" : "Customer and project support", source, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "วิธีสมัครงาน" : "How to apply", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"google-site-verification\"", source, StringComparison.Ordinal);
        Assert.Contains("Manufacturing Engineer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Filled role", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown status role", source, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wowjs", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate__", source, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(new CareerRequest(CareerSort.JobId_Ascending, " engineer ", 1, 50), clientStub.LastRequest);
    }

    [Fact]
    public async Task LocalReviewCareerRoute_HidesOnlyTheExactAspireFixture()
    {
        var clientStub = new CapturingCareerClient(includeLocalAspireFixture: true);
        await using var routeFactory = CreateFactory(clientStub, hideLocalAspireFixture: true);
        using var client = CreateClient(routeFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/career?culture=en"));

        Assert.Contains("Manufacturing Engineer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Local Manufacturing Engineer", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CareerRoute_NormalizesSortAndIndexAndPreservesCanonicalDocumentLinks()
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub);
        using var client = CreateClient(routeFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync(
            "/career?culture=en&sort=not-a-sort&index=-4&size=100&tracking=excluded"));

        Assert.Equal(new CareerRequest(CareerSort.JobId_Ascending, null, -4, 100), clientStub.LastRequest);
        Assert.Equal(1, CountLink(source, "canonical", "https://www.maliev.com/career?culture=en"));
        Assert.Equal(1, CountAlternate(source, "en", "https://www.maliev.com/career?culture=en"));
        Assert.Equal(1, CountAlternate(source, "th", "https://www.maliev.com/career"));
        Assert.Equal(1, CountAlternate(source, "x-default", "https://www.maliev.com/career"));
        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("2147483648")]
    public async Task CareerRoute_RejectsOutOfRangePageSizeBeforeCallingCareerService(string size)
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync($"/career?size={size}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(clientStub.LastRequest);
    }

    public static IEnumerable<object[]> InvalidHandlerPageSizes()
    {
        foreach (var rendererEnabled in new[] { true, false })
        {
            foreach (var culture in new[] { "en", "th" })
            {
                foreach (var handler in new[] { "Search", "ChangeItemCount" })
                {
                    foreach (var size in new[] { "invalid-size", "0", "-1", "101", "2147483648" })
                    {
                        yield return new object[] { rendererEnabled, culture, handler, size };
                    }
                }

                yield return new object[] { rendererEnabled, culture, "ChangeItemCount", string.Empty };
            }
        }
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    public async Task CareerPageSizeHandler_RequiresExplicitSize(bool rendererEnabled, string culture)
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub, careerRouteEnabled: rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync($"/career?culture={culture}&handler=ChangeItemCount");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(clientStub.LastRequest);
    }

    [Theory]
    [MemberData(nameof(InvalidHandlerPageSizes))]
    public async Task CareerHandlers_RejectInvalidPageSizeBeforeCallingCareerService(
        bool rendererEnabled,
        string culture,
        string handler,
        string size)
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub, careerRouteEnabled: rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync($"/career?culture={culture}&handler={handler}&size={size}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(clientStub.LastRequest);
    }

    [Theory]
    [InlineData(true, "en", 1)]
    [InlineData(true, "th", 100)]
    [InlineData(false, "en", 100)]
    [InlineData(false, "th", 1)]
    public async Task CareerSearchHandler_PreservesOriginalJsonFieldsAndBoundQuery(
        bool rendererEnabled,
        string culture,
        int size)
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub, careerRouteEnabled: rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync(
            $"/career?culture={culture}&handler=Search&sort=JobId_Ascending&search=%20engineer%20&index=3&size={size}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(1, document.RootElement.GetArrayLength());
        var offer = document.RootElement[0];
        Assert.Equal(
            new[] { "createdDate", "description", "id", "introduction", "isFilled", "level", "levelId", "location", "modifiedDate", "prerequisites", "title", "whatWeOffer" },
            offer.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(1, offer.GetProperty("id").GetInt32());
        Assert.Equal(1, offer.GetProperty("levelId").GetInt32());
        Assert.Equal("Manufacturing Engineer", offer.GetProperty("title").GetString());
        Assert.Equal("Nonthaburi", offer.GetProperty("location").GetString());
        Assert.False(offer.GetProperty("isFilled").GetBoolean());
        foreach (var name in new[] { "createdDate", "description", "introduction", "modifiedDate", "prerequisites", "whatWeOffer" })
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, offer.GetProperty(name).ValueKind);
        }

        var level = offer.GetProperty("level");
        Assert.Equal(new[] { "createdDate", "description", "id", "modifiedDate", "name", "offers" },
            level.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(0, level.GetProperty("offers").GetArrayLength());
        Assert.Equal(1, level.GetProperty("id").GetInt32());
        Assert.Equal("Engineer", level.GetProperty("name").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, level.GetProperty("description").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, level.GetProperty("createdDate").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, level.GetProperty("modifiedDate").ValueKind);
        Assert.Equal(new CareerRequest(CareerSort.JobId_Ascending, " engineer ", 1, size), clientStub.LastRequest);
        Assert.Equal(0, clientStub.ListingCalls);
        Assert.Equal(1, clientStub.OffersCalls);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task CareerSearchHandler_FiltersOnlyClosedAndUnknownOffersWithoutHidingSourceVisibleFixture(
        bool rendererEnabled, bool hideFixture)
    {
        var stub = new CapturingCareerClient(includeLocalAspireFixture: true, includeNearFixture: true);
        await using var routeFactory = CreateFactory(stub, rendererEnabled, hideFixture);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync("/career?handler=Search&size=25");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { 1, 4, 5 },
            document.RootElement.EnumerateArray().Select(offer => offer.GetProperty("id").GetInt32()).ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CareerSearchHandler_HeadUsesSearchContractWithoutResponseBody(bool rendererEnabled)
    {
        var stub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(stub, rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var request = new HttpRequestMessage(HttpMethod.Head, "/career?handler=Search&size=25");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Equal(new CareerRequest(CareerSort.JobId_Ascending, null, 1, 25), stub.LastRequest);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CareerSearchHandler_PreservesRetainedUnavailableEmptyArray(bool rendererEnabled)
    {
        var stub = new CapturingCareerClient(serviceAvailable: false);
        await using var routeFactory = CreateFactory(stub, rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync("/career?handler=Search");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
        Assert.Equal(new CareerRequest(CareerSort.JobId_Ascending, null, 1, 25), stub.LastRequest);
    }

    [Theory]
    [InlineData(true, "invalid")]
    [InlineData(true, "2147483648")]
    [InlineData(false, "invalid")]
    [InlineData(false, "2147483648")]
    public async Task CareerSearchHandler_RejectsMalformedIndexBeforeServiceRead(bool rendererEnabled, string index)
    {
        var stub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(stub, rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync($"/career?handler=Search&index={index}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(stub.LastRequest);
    }

    [Theory]
    [InlineData(true, "Search&handler=ChangeItemCount&size=1&size=invalid", 200, "application/json", 1)]
    [InlineData(false, "Search&handler=ChangeItemCount&size=1&size=invalid", 200, "application/json", 1)]
    [InlineData(true, "ChangeItemCount&handler=Search&size=1&size=invalid", 200, "text/html", 0)]
    [InlineData(false, "ChangeItemCount&handler=Search&size=1&size=invalid", 200, "text/html", 0)]
    [InlineData(true, "Search&handler=ChangeItemCount&size=invalid&size=1", 400, null, null)]
    [InlineData(false, "Search&handler=ChangeItemCount&size=invalid&size=1", 400, null, null)]
    public async Task CareerHandlers_UseFirstDuplicateHandlerAndSizeValuesLikeRetainedBinding(
        bool rendererEnabled, string query, int status, string? mediaType, int? expectedIndex)
    {
        var stub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(stub, rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync($"/career?handler={query}");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        if (mediaType is not null)
        {
            Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(new CareerRequest(CareerSort.JobId_Ascending, null,
                expectedIndex ?? throw new InvalidOperationException("A successful handler case requires its source index."), 1), stub.LastRequest);
        }
        else
        {
            Assert.Null(stub.LastRequest);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task CareerSearchHandler_PreservesCancellationAndUnexpectedFailurePropagation(
        bool rendererEnabled, bool canceled)
    {
        using var cancellation = new CancellationTokenSource();
        if (canceled)
        {
            cancellation.Cancel();
        }

        Exception expected = canceled
            ? new OperationCanceledException(cancellation.Token)
            : new InvalidOperationException("career boundary failure");
        var stub = new FailingCareerClient(expected);
        using var services = new ServiceCollection().AddSingleton<ICareerClient>(stub).BuildServiceProvider();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = services,
            RequestAborted = cancellation.Token
        };
        context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?handler=Search&size=25");
        var retained = new Legacy.Maliev.Web.Pages.About.Career.Index(
            stub, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
        {
            PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = context }
        };
        // Keep the regression assembly buildable when only tests are overlaid on the pre-fix baseline.
        // The selected causal HTTP cases then reach the original route instead of failing compilation.
        Func<Task> invoke = rendererEnabled
            ? () => InvokeSearchAdapterAsync(context)
            : async () => { await retained.OnGetSearchAsync(null, null, null, 25, cancellation.Token); };
        var actual = await Record.ExceptionAsync(invoke);

        Assert.Same(expected, actual);
        Assert.Equal(cancellation.Token, stub.LastCancellationToken);
    }

    private static Task InvokeSearchAdapterAsync(Microsoft.AspNetCore.Http.HttpContext context)
    {
        var adapter = typeof(Program).Assembly.GetType(
            "Legacy.Maliev.Web.Pages.Shared.CareerSearchCompatibilityEndpoint", throwOnError: true)!;
        var method = adapter.GetMethod("HandleAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException(adapter.FullName, "HandleAsync");
        return method.CreateDelegate<Func<Microsoft.AspNetCore.Http.HttpContext, Task>>()(context);
    }

    [Theory]
    [InlineData(true, "en", 1)]
    [InlineData(true, "th", 100)]
    [InlineData(false, "en", 100)]
    [InlineData(false, "th", 1)]
    public async Task CareerPageSizeHandler_ReturnsLocalizedDocumentAndResetsSearchAndIndex(
        bool rendererEnabled,
        string culture,
        int size)
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub, careerRouteEnabled: rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync(
            $"/career?culture={culture}&handler=ChangeItemCount&sort=JobId_Ascending&search=engineer&index=3&size={size}");
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"<html lang=\"{culture}\"", body, StringComparison.Ordinal);
        Assert.Contains("Manufacturing Engineer", body, StringComparison.Ordinal);
        Assert.Equal(new CareerRequest(CareerSort.JobId_Ascending, null, 0, size), clientStub.LastRequest);
    }

    [Fact]
    public async Task CareerRoute_RejectsMalformedPageSize()
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync("/career?handler=ChangeItemCount&size=invalid-size");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(clientStub.LastRequest);
    }

    [Theory]
    [InlineData(true, 200)]
    [InlineData(false, 200)]
    [InlineData(true, 404)]
    [InlineData(false, 404)]
    [InlineData(true, 400)]
    [InlineData(false, 400)]
    [InlineData(true, 500)]
    [InlineData(false, 500)]
    [InlineData(true, 503)]
    [InlineData(false, 503)]
    public async Task Search_ActualTypedClient_PreservesOfferOnlyHttpAndSourceJsonOutcome(
        bool rendererEnabled, int upstreamStatus)
    {
        using var handler = new StrictOfferHandler(upstreamStatus);
        using var upstream = new HttpClient(handler) { BaseAddress = new Uri("http://career-source-fixture/") };
        var typed = new CareerClient(new FixedCareerFactory(upstream), NullLogger<CareerClient>.Instance);
        await using var routeFactory = CreateFactory(typed, rendererEnabled, hideLocalAspireFixture: true);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync("/career?handler=Search&search=%20engineer%20&index=9&size=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/Jobs", request.AbsolutePath);
        Assert.Contains("sort=JobId_Ascending", request.Query, StringComparison.Ordinal);
        Assert.Contains("search=%20engineer%20", request.Query, StringComparison.Ordinal);
        Assert.Contains("index=1", request.Query, StringComparison.Ordinal);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(System.Text.Json.JsonValueKind.Array, document.RootElement.ValueKind);
        if (upstreamStatus != 200)
        {
            Assert.Empty(document.RootElement.EnumerateArray().ToArray());
            return;
        }

        var offer = Assert.Single(document.RootElement.EnumerateArray().ToArray());
        Assert.Equal(new[] { "createdDate", "description", "id", "introduction", "isFilled", "level", "levelId", "location", "modifiedDate", "prerequisites", "title", "whatWeOffer" },
            offer.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        var level = offer.GetProperty("level");
        Assert.Equal(new[] { "createdDate", "description", "id", "modifiedDate", "name", "offers" },
            level.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Empty(level.GetProperty("offers").EnumerateArray().ToArray());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, offer.GetProperty("createdDate").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, level.GetProperty("modifiedDate").ValueKind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_ActualTypedClient_UnexpectedFailureUsesPrivateHostError(bool rendererEnabled)
    {
        using var handler = new StrictOfferHandler(200, unexpectedFailure: true);
        using var upstream = new HttpClient(handler) { BaseAddress = new Uri("http://career-source-fixture/") };
        var typed = new CareerClient(new FixedCareerFactory(upstream), NullLogger<CareerClient>.Instance);
        await using var routeFactory = CreateFactory(typed, rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync("/career?handler=Search");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("synthetic-private-career-failure", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_ActualTypedClient_PropagatesRequestAbortToOfferRead(bool rendererEnabled)
    {
        using var handler = new StrictOfferHandler(200, waitForAbort: true);
        using var upstream = new HttpClient(handler) { BaseAddress = new Uri("http://career-source-fixture/") };
        var typed = new CareerClient(new FixedCareerFactory(upstream), NullLogger<CareerClient>.Instance);
        await using var routeFactory = CreateFactory(typed, rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var pending = client.GetAsync("/career?handler=Search", cancellation.Token);
        try
        {
            await handler.SendObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            await handler.AbortObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Single(handler.Requests);
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                using var response = await pending.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [Fact]
    public async Task DisabledCareerRoute_UsesTheRetainedRazorFallback()
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub, careerRouteEnabled: false);
        using var client = CreateClient(routeFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/career?culture=en"));

        Assert.Contains("<title>Career | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Equal(new CareerRequest(CareerSort.JobId_Ascending, null, 1, 25), clientStub.LastRequest);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_PreservesOriginalNullableLevelInJson(bool rendererEnabled)
    {
        var stub = new CapturingCareerClient(nullLevel: true);
        await using var routeFactory = CreateFactory(stub, rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync("/career?handler=Search");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var offer = Assert.Single(document.RootElement.EnumerateArray().ToArray());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, offer.GetProperty("level").ValueKind);
    }

    public static IEnumerable<object?[]> OriginalNormalQueries()
    {
        foreach (var renderer in new[] { true, false })
        {
            foreach (var culture in new[] { "en", "th" })
            {
                yield return [renderer, culture, "0", null, -2, CareerSort.JobId_Ascending, null, -2];
                yield return [renderer, culture, "unknown", " engineer ", 0, CareerSort.JobId_Ascending, " engineer ", 1];
                yield return [renderer, culture, "1", string.Empty, 0, CareerSort.JobId_Descending, null, 0];
                yield return [renderer, culture, "JobCreatedDate_Descending", "  ", -2, CareerSort.JobCreatedDate_Descending, null, -2];
            }
        }
    }

    public static IEnumerable<object?[]> OriginalNestedOffersCases()
    {
        foreach (var renderer in new[] { true, false })
        {
            foreach (var culture in new[] { "en", "th" })
            {
                yield return [renderer, culture, null, 0];
                yield return [renderer, culture, ",\"offers\":null", -1];
                yield return [renderer, culture, ",\"offers\":[]", 0];
                yield return [renderer, culture, ",\"offers\":[{\"id\":9,\"levelId\":3,\"isFilled\":false}]", 1];
            }
        }
    }

    [Theory]
    [MemberData(nameof(OriginalNestedOffersCases))]
    public async Task Search_ActualTypedClient_MatchesOriginalNestedOffersOracle(
        bool rendererEnabled, string culture, string? nestedMember, int expectedCount)
    {
        using var handler = new StrictOfferHandler(200, nestedOffersMember: nestedMember);
        using var upstream = new HttpClient(handler) { BaseAddress = new Uri("http://career-source-fixture/") };
        var typed = new CareerClient(new FixedCareerFactory(upstream), NullLogger<CareerClient>.Instance);
        await using var routeFactory = CreateFactory(typed, rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync($"/career?handler=Search&culture={culture}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("/Jobs", Assert.Single(handler.Requests).AbsolutePath);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var offer = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(12, offer.EnumerateObject().Count());
        Assert.Equal(42, offer.GetProperty("id").GetInt32());
        Assert.Equal(3, offer.GetProperty("levelId").GetInt32());
        Assert.Equal("Engineer", offer.GetProperty("title").GetString());
        Assert.Equal("Nonthaburi", offer.GetProperty("location").GetString());
        Assert.False(offer.GetProperty("isFilled").GetBoolean());
        foreach (var name in new[] { "introduction", "description", "prerequisites", "whatWeOffer", "createdDate", "modifiedDate" })
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, offer.GetProperty(name).ValueKind);
        }

        var level = offer.GetProperty("level");
        Assert.Equal(6, level.EnumerateObject().Count());
        Assert.Equal(3, level.GetProperty("id").GetInt32());
        Assert.Equal("Senior", level.GetProperty("name").GetString());
        foreach (var name in new[] { "description", "createdDate", "modifiedDate" })
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, level.GetProperty(name).ValueKind);
        }

        var collection = level.GetProperty("offers");
        if (expectedCount < 0)
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, collection.ValueKind);
        }
        else
        {
            Assert.Equal(expectedCount, collection.GetArrayLength());
            if (expectedCount == 1)
            {
                var child = Assert.Single(collection.EnumerateArray());
                Assert.Equal(12, child.EnumerateObject().Count());
                Assert.Equal(9, child.GetProperty("id").GetInt32());
                Assert.Equal(3, child.GetProperty("levelId").GetInt32());
                Assert.False(child.GetProperty("isFilled").GetBoolean());
                foreach (var name in new[] { "title", "introduction", "description", "prerequisites", "whatWeOffer", "location", "createdDate", "modifiedDate", "level" })
                {
                    Assert.Equal(System.Text.Json.JsonValueKind.Null, child.GetProperty(name).ValueKind);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(OriginalNormalQueries))]
    public async Task NormalGet_ActualTypedClient_PreservesOriginalQueryAndBothReads(
        bool rendererEnabled, string culture, string sort, string? search, int index,
        CareerSort expectedSort, string? expectedSearch, int expectedIndex)
    {
        using var handler = new StrictOfferHandler(200, allowLevels: true);
        using var upstream = new HttpClient(handler) { BaseAddress = new Uri("http://career-source-fixture/") };
        var typed = new CareerClient(new FixedCareerFactory(upstream), NullLogger<CareerClient>.Instance);
        await using var routeFactory = CreateFactory(typed, rendererEnabled);
        using var client = CreateClient(routeFactory);
        var searchQuery = search is null ? string.Empty : $"&search={Uri.EscapeDataString(search)}";
        using var response = await client.GetAsync($"/career?culture={culture}&sort={sort}&index={index}{searchQuery}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(handler.Requests, uri => uri.AbsolutePath == "/jobs/levels");
        var offers = Assert.Single(handler.Requests, uri => uri.AbsolutePath == "/Jobs");
        Assert.Contains($"sort={expectedSort}", offers.Query, StringComparison.Ordinal);
        Assert.Contains($"search={Uri.EscapeDataString(expectedSearch ?? string.Empty)}", offers.Query, StringComparison.Ordinal);
        Assert.Contains($"index={expectedIndex}", offers.Query, StringComparison.Ordinal);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"<html lang=\"{culture}\"", body, StringComparison.Ordinal);
        Assert.Contains("Engineer", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    public async Task NormalGet_ActualTypedClient_PreservesLevelsAvailabilityDependence(
        bool rendererEnabled, string culture)
    {
        using var handler = new StrictOfferHandler(200, allowLevels: true, levelStatus: 503);
        using var upstream = new HttpClient(handler) { BaseAddress = new Uri("http://career-source-fixture/") };
        var typed = new CareerClient(new FixedCareerFactory(upstream), NullLogger<CareerClient>.Instance);
        await using var routeFactory = CreateFactory(typed, rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync($"/career?culture={culture}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(handler.Requests, uri => uri.AbsolutePath == "/jobs/levels");
        Assert.Contains("role=\"status\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, null, -2, -2)]
    [InlineData(false, null, -2, -2)]
    [InlineData(true, "", 0, 0)]
    [InlineData(false, "", 0, 0)]
    [InlineData(true, "  ", -2, -2)]
    [InlineData(false, "  ", -2, -2)]
    [InlineData(true, " engineer ", 9, 1)]
    [InlineData(false, " engineer ", 9, 1)]
    public async Task Search_PreservesOriginalBoundSearchAndIndexAndUsesOnlyOfferRead(
        bool rendererEnabled, string? search, int index, int expectedIndex)
    {
        var stub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(stub, rendererEnabled);
        using var client = CreateClient(routeFactory);
        var searchQuery = search is null ? string.Empty : $"&search={Uri.EscapeDataString(search)}";
        using var response = await client.GetAsync($"/career?handler=Search&index={index}{searchQuery}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new CareerRequest(CareerSort.JobId_Ascending, string.IsNullOrWhiteSpace(search) ? null : search, expectedIndex, 25), stub.LastRequest);
        Assert.Equal(0, stub.ListingCalls);
        Assert.Equal(1, stub.OffersCalls);
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    public async Task ChangeItemCount_IgnoresUnboundQueryAndPreservesOriginalZeroIndex(
        bool rendererEnabled, string culture)
    {
        var stub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(stub, rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync(
            $"/career?handler=ChangeItemCount&culture={culture}&sort=unknown&search=ignored&index=not-an-int&size=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new CareerRequest(CareerSort.JobId_Ascending, null, 0, 1), stub.LastRequest);
        Assert.Equal(1, stub.ListingCalls);
        Assert.Equal(0, stub.OffersCalls);
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    public async Task ChangeItemCount_DoesNotHideAnOriginallyVisibleOffer(
        bool rendererEnabled, string culture)
    {
        var stub = new CapturingCareerClient(includeLocalAspireFixture: true);
        await using var routeFactory = CreateFactory(stub, rendererEnabled, hideLocalAspireFixture: true);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync($"/career?handler=ChangeItemCount&culture={culture}&size=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Local Manufacturing Engineer", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("invalid-size")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("2147483648")]
    public async Task DisabledCareerRoute_RejectsInvalidPageSizeBeforeCallingCareerService(string size)
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub, careerRouteEnabled: false);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync($"/career?size={size}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(clientStub.LastRequest);
    }

    private WebApplicationFactory<Program> CreateFactory(
        ICareerClient careerClient,
        bool careerRouteEnabled = true,
        bool hideLocalAspireFixture = false) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:CareerIndex", careerRouteEnabled.ToString());
            builder.UseSetting("Career:HideLocalAspireFixture", hideLocalAspireFixture.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICareerClient>();
                services.AddSingleton(careerClient);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> sourceFactory) => sourceFactory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static string ExtractDocumentLinks(string source) => string.Join(
        Environment.NewLine,
        System.Text.RegularExpressions.Regex.Matches(
                source,
                "<link[^>]+(?:rel=\"canonical\"|hreflang=\"(?:en|th|x-default)\")[^>]*>",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            .Select(match => match.Value));

    private static int CountLink(string source, string relation, string url) =>
        System.Text.RegularExpressions.Regex.Matches(
            source,
            $"<link(?=[^>]*rel=\"{System.Text.RegularExpressions.Regex.Escape(relation)}\")(?=[^>]*href=\"{System.Text.RegularExpressions.Regex.Escape(url)}\")[^>]*>",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant).Count;

    private static int CountAlternate(string source, string language, string url) =>
        System.Text.RegularExpressions.Regex.Matches(
            source,
            $"<link(?=[^>]*rel=\"alternate\")(?=[^>]*href=\"{System.Text.RegularExpressions.Regex.Escape(url)}\")(?=[^>]*hreflang=\"{System.Text.RegularExpressions.Regex.Escape(language)}\")[^>]*>",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant).Count;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class CapturingCareerClient(
        bool includeLocalAspireFixture = false, bool serviceAvailable = true, bool includeNearFixture = false,
        bool nullLevel = false) : ICareerClient
    {
        // Deserialize the source wire fixture so a tests-only baseline overlay does not require the new DTO property.
        private static readonly CareerLevel Level = System.Text.Json.JsonSerializer.Deserialize<CareerLevel>(
            """
            {"Id":1,"Name":"Engineer","Description":null,"CreatedDate":null,"ModifiedDate":null,"Offers":[]}
            """)!;

        public int ListingCalls { get; private set; }
        public int OffersCalls { get; private set; }
        public CareerRequest? LastRequest { get; private set; }

        public async Task<ServiceResponse<IReadOnlyList<CareerLevel>>> GetLevelsAsync(CancellationToken cancellationToken)
        {
            var listing = await GetListingAsync(CareerSort.JobId_Ascending, null, 1, 25, cancellationToken);
            return new ServiceResponse<IReadOnlyList<CareerLevel>>(listing.Levels, listing.ServiceAvailable);
        }

        public async Task<ServiceResponse<CareerOfferPage>> GetOffersAsync(
            CareerSort sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            OffersCalls++;
            var listing = await GetSyntheticListingAsync(sort, search, pageIndex, pageSize, cancellationToken);
            return new ServiceResponse<CareerOfferPage>(listing.Offers, listing.ServiceAvailable);
        }

        public Task<CareerListing> GetListingAsync(
            CareerSort sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            ListingCalls++;
            return GetSyntheticListingAsync(sort, search, pageIndex, pageSize, cancellationToken);
        }

        private Task<CareerListing> GetSyntheticListingAsync(
            CareerSort sort,
            string? search,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken)
        {
            LastRequest = new CareerRequest(sort, search, pageIndex, pageSize);
            if (!serviceAvailable)
            {
                return Task.FromResult(new CareerListing([], CareerOfferPage.Empty(pageIndex), false));
            }
            var offers = new List<CareerOffer>
            {
                new(1, 1, "Manufacturing Engineer", null, null, null, null, "Nonthaburi", false, null, null, nullLevel ? null : Level),
                new(2, 1, "Filled role", null, null, null, null, "Bangkok", true, null, null, Level),
                new(3, 1, "Unknown status role", null, null, null, null, "Bangkok", null, null, null, Level),
            };
            if (includeLocalAspireFixture)
            {
                offers.Add(new CareerOffer(
                    4,
                    1,
                    "Local Manufacturing Engineer",
                    "Local Aspire career boundary verification",
                    "Support digital manufacturing projects.",
                    "Manufacturing experience",
                    "Independent engineering work",
                    "Nonthaburi",
                    false,
                    new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                    null,
                    new CareerLevel(
                        1,
                        "Experienced",
                        "Local Aspire verification level",
                        new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                        null)));
                if (includeNearFixture)
                {
                    offers.Add(offers[^1] with { Id = 5, Description = "A real opening with a different description." });
                }
            }

            return Task.FromResult(new CareerListing(
                [Level],
                new CareerOfferPage(
                    offers,
                    Math.Max(pageIndex, 1),
                    3,
                    6,
                    pageIndex > 1,
                    pageIndex < 3),
                true));
        }

        public Task<ServiceResponse<CareerOffer>> GetOfferAsync(int offerId, CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<CareerOffer>(null, true));
    }

    private sealed record CareerRequest(CareerSort Sort, string? Search, int PageIndex, int PageSize);

    private sealed class FixedCareerFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("careers", name);
            return client;
        }
    }

    private sealed class StrictOfferHandler(
        int status, bool unexpectedFailure = false, bool waitForAbort = false,
        bool allowLevels = false, int levelStatus = 200, string? nestedOffersMember = null) : HttpMessageHandler
    {
        public System.Collections.Concurrent.ConcurrentQueue<Uri> Requests { get; } = new();
        public TaskCompletionSource AbortObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SendObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing offer URI.");
            Requests.Enqueue(uri);
            Assert.Null(request.Headers.Authorization);
            if (allowLevels && uri.AbsolutePath == "/jobs/levels")
            {
                return new HttpResponseMessage((HttpStatusCode)levelStatus)
                {
                    Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
                };
            }

            Assert.Equal("/Jobs", uri.AbsolutePath);
            if (unexpectedFailure)
            {
                throw new InvalidOperationException("synthetic-private-career-failure");
            }

            if (waitForAbort)
            {
                SendObserved.TrySetResult();
                using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                expiry.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, expiry.Token);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested) AbortObserved.TrySetResult();
                }
            }

            return new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent("""
                    {"items":[{"id":42,"levelId":3,"title":"Engineer","introduction":null,"description":null,"prerequisites":null,"whatWeOffer":null,"location":"Nonthaburi","isFilled":false,"createdDate":null,"modifiedDate":null,"level":{"id":3,"name":"Senior","description":null,"createdDate":null,"modifiedDate":null,"offers":[]}}],"pageIndex":1,"totalPages":1,"totalItems":1,"hasPreviousPage":false,"hasNextPage":false}
                    """.Replace(",\"offers\":[]", nestedOffersMember ?? string.Empty, StringComparison.Ordinal),
                    System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FailingCareerClient(Exception failure) : ICareerClient
    {
        public CancellationToken LastCancellationToken { get; private set; }

        public async Task<ServiceResponse<IReadOnlyList<CareerLevel>>> GetLevelsAsync(CancellationToken cancellationToken)
        {
            var listing = await GetListingAsync(CareerSort.JobId_Ascending, null, 1, 25, cancellationToken);
            return new ServiceResponse<IReadOnlyList<CareerLevel>>(listing.Levels, listing.ServiceAvailable);
        }

        public async Task<ServiceResponse<CareerOfferPage>> GetOffersAsync(
            CareerSort sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            var listing = await GetListingAsync(sort, search, pageIndex, pageSize, cancellationToken);
            return new ServiceResponse<CareerOfferPage>(listing.Offers, listing.ServiceAvailable);
        }

        public Task<CareerListing> GetListingAsync(
            CareerSort sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromException<CareerListing>(failure);
        }

        public Task<ServiceResponse<CareerOffer>> GetOfferAsync(int offerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

[Collection(CncNativeBrowserCollection.Name)]
public sealed class CareerSearchKestrelContractTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task CareerSearchHead_PreservesJsonHeadersAndSuppressesBodyOnRealKestrel(string culture)
    {
        var origin = new Uri(fixture.CncQuotationUrl);
        var route = new Uri(origin, $"/career?culture={culture}&handler=Search&size=25");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var get = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("application/json", get.Content.Headers.ContentType?.MediaType);
        Assert.True(get.Headers.CacheControl?.NoStore == true);
        using var document = System.Text.Json.JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal(System.Text.Json.JsonValueKind.Array, document.RootElement.ValueKind);

        using var request = new HttpRequestMessage(HttpMethod.Head, route);
        using var head = await client.SendAsync(request);
        Assert.Equal(get.StatusCode, head.StatusCode);
        Assert.Equal(get.Content.Headers.ContentType?.ToString(), head.Content.Headers.ContentType?.ToString());
        Assert.True(head.Headers.CacheControl?.NoStore == true);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    }
}
