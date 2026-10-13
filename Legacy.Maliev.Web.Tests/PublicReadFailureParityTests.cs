using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Contact;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using ContactPage = Legacy.Maliev.Web.Pages.Contact.Index;
using CareerView = Legacy.Maliev.Web.Pages.About.Career.View;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Original transport and missing-offer assertions through real typed clients and public renderers.</summary>
public sealed class PublicReadFailureParityTests : IClassFixture<TestingWebApplicationFactory>
{
    private const string CountryError = "Could not retrieve countries from the server";
    private readonly TestingWebApplicationFactory factory;
    private readonly ITestOutputHelper output;

    public PublicReadFailureParityTests(TestingWebApplicationFactory factory, ITestOutputHelper output)
    {
        this.factory = factory;
        this.output = output;
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(null, false)]
    [InlineData("available", true)]
    [InlineData("available", false)]
    public void PrivateStatus_PreservesRecordEqualityHashTextDeconstructionAndJson(string? value, bool available)
    {
        var baseline = new ServiceResponse<string>(value, available);
        // This unchanged two-position record is the pre-metadata declaration witness.
        var original = new OriginalServiceResponse<string>(value, available);
        var provenance = baseline with { UpstreamStatus = HttpStatusCode.NotFound };
        Assert.True(baseline == provenance);
        Assert.True(provenance == baseline);
        Assert.True(baseline.Equals((object)provenance));
        Assert.Equal(baseline.GetHashCode(), provenance.GetHashCode());
        var typeHashDifference = unchecked(EqualityComparer<Type>.Default.GetHashCode(typeof(ServiceResponse<string>))
            - EqualityComparer<Type>.Default.GetHashCode(typeof(OriginalServiceResponse<string>)));
        var expectedOldHashWithCurrentType = unchecked(original.GetHashCode()
            + (typeHashDifference * -1521134295 * -1521134295));
        Assert.Equal(expectedOldHashWithCurrentType, provenance.GetHashCode());
        Assert.Contains(provenance, new HashSet<ServiceResponse<string>> { baseline });
        var (actualValue, actualAvailable) = provenance;
        Assert.Equal(value, actualValue);
        Assert.Equal(available, actualAvailable);
        Assert.Equal($"ServiceResponse {{ Value = {value}, ServiceAvailable = {available} }}", provenance.ToString());
        Assert.Equal(baseline.ToString(), provenance.ToString());
        Assert.Equal(original.ToString().Replace("OriginalServiceResponse", "ServiceResponse", StringComparison.Ordinal), provenance.ToString());
        var json = JsonSerializer.Serialize(provenance);
        Assert.Equal(JsonSerializer.Serialize(baseline), json);
        Assert.Equal(JsonSerializer.Serialize(original), json);
        Assert.DoesNotContain("UpstreamStatus", json, StringComparison.Ordinal);
        Assert.Null(JsonSerializer.Deserialize<ServiceResponse<string>>(json)!.UpstreamStatus);
        Assert.NotEqual(baseline, baseline with { ServiceAvailable = !available });
        Assert.NotEqual(baseline, baseline with { Value = "different" });
    }

    [Fact]
    public async Task ContactComponent_CallerCancellationEscapesWithoutManufacturingIncident()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var countries = new CancelledCountries();
        var component = new Legacy.Maliev.Web.Components.Pages.Contact.ContactPage();
        var componentType = component.GetType();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        componentType.GetProperty("HttpContextAccessor", flags)!.SetValue(component, new HttpContextAccessor { HttpContext = context });
        componentType.GetProperty("CountryClient", flags)!.SetValue(component, countries);
        var initialize = (Task)componentType.GetMethod("OnInitializedAsync", flags)!.Invoke(component, null)!;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialize);
        Assert.Equal(1, countries.Calls);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("X-Incident-Id"));
    }

    [Fact]
    public void PrivateStatus_CannotBeSuppliedThroughJson()
    {
        var result = JsonSerializer.Deserialize<ServiceResponse<string>>(
            "{\"Value\":null,\"ServiceAvailable\":true,\"UpstreamStatus\":404}")!;
        Assert.True(result.ServiceAvailable);
        Assert.Null(result.Value);
        Assert.Null(result.UpstreamStatus);
    }

    [Theory]
    [InlineData(ResponseMode.Success)]
    [InlineData(ResponseMode.EmptySuccess)]
    [InlineData(ResponseMode.Transport)]
    [InlineData(ResponseMode.Unavailable)]
    [InlineData(ResponseMode.Missing)]
    [InlineData(ResponseMode.Unexpected)]
    public async Task ContactGet_OriginalTransportAssertionAndExistingControls(ResponseMode mode)
    {
        using var upstream = new Upstream(mode, countries: true);
        var page = CreateContact(upstream.Country);
        if (mode == ResponseMode.Unexpected)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => page.OnGetAsync(CancellationToken.None));
        }
        else
        {
            Assert.IsType<PageResult>(await page.OnGetAsync(CancellationToken.None));
            var failed = mode is ResponseMode.Transport or ResponseMode.Unavailable or ResponseMode.Missing;
            Assert.Equal(!failed, page.CountryServiceAvailable);
            if (mode == ResponseMode.Success)
            {
                Assert.Equal("Thailand", Assert.Single(page.Countries).Name);
                Assert.True(page.ModelState.IsValid);
            }
            else
            {
                Assert.Empty(page.Countries);
                if (failed)
                {
                    Assert.Equal(CountryError, Assert.Single(page.ModelState[string.Empty]!.Errors).ErrorMessage);
                }
                else
                {
                    // Successful empty catalogues remain available without a validation error.
                    Assert.True(page.ModelState.IsValid);
                }
            }
        }
        Assert.Equal("/Countries", Assert.Single(upstream.Paths));
    }

    [Theory]
    [InlineData(ResponseMode.Success)]
    [InlineData(ResponseMode.Missing)]
    [InlineData(ResponseMode.Transport)]
    [InlineData(ResponseMode.Unavailable)]
    [InlineData(ResponseMode.Unexpected)]
    public async Task CareerGet_OriginalMaxIntMissingOfferAndExistingControls(ResponseMode mode)
    {
        using var upstream = new Upstream(mode, countries: false);
        var page = new CareerView(upstream.Career, new ConfigurationBuilder().Build());
        if (mode == ResponseMode.Unexpected)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => page.OnGetAsync(int.MaxValue, CancellationToken.None));
        }
        else
        {
            var result = await page.OnGetAsync(int.MaxValue, CancellationToken.None);
            if (mode == ResponseMode.Missing)
            {
                Assert.IsType<NotFoundResult>(result);
            }
            else if (mode == ResponseMode.Success)
            {
                Assert.IsType<PageResult>(result);
                Assert.Equal(int.MaxValue, page.JobOffer.Id);
            }
            else
            {
                Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeResult>(result).StatusCode);
            }
        }
        Assert.Equal("/Jobs/2147483647", Assert.Single(upstream.Paths));
        Assert.DoesNotContain("/jobs/levels", upstream.Paths);
    }

    public static IEnumerable<object[]> RouteCases()
    {
        foreach (var culture in new[] { "en", "th" })
        {
            foreach (var enabled in new[] { true, false })
            {
                foreach (var mode in Enum.GetValues<ResponseMode>().Where(mode => mode != ResponseMode.EmptySuccess))
                {
                    yield return [culture, enabled, mode];
                }
            }
        }
    }

    public static IEnumerable<object[]> ContactRouteCases() => RouteCases().Concat(
        new[] { "en", "th" }.SelectMany(culture => new[] { true, false }.Select(enabled =>
            new object[] { culture, enabled, ResponseMode.EmptySuccess })));

    [Fact]
    public async Task Country404AndSuccessfulEmptyCatalogue_RetainAvailabilityWithDistinctPrivateProvenance()
    {
        using var missing = new Upstream(ResponseMode.Missing, countries: true);
        using var empty = new Upstream(ResponseMode.EmptySuccess, countries: true);
        var missingResult = await missing.Country.GetCountriesAsync(CancellationToken.None);
        var emptyResult = await empty.Country.GetCountriesAsync(CancellationToken.None);
        Assert.True(missingResult.ServiceAvailable);
        Assert.True(emptyResult.ServiceAvailable);
        Assert.Empty(missingResult.Value!);
        Assert.Empty(emptyResult.Value!);
        Assert.Equal(HttpStatusCode.NotFound, missingResult.UpstreamStatus);
        Assert.Null(emptyResult.UpstreamStatus);
    }

    [Theory]
    [MemberData(nameof(ContactRouteCases))]
    public async Task ContactRoute_ActualHttpRendersBothLanguagesWithoutUpstreamLeak(string culture, bool enabled, ResponseMode mode)
    {
        using var upstream = new Upstream(mode, countries: true);
        using var events = new BoundaryLogProvider();
        await using var host = CreateFactory(upstream, "Contact", enabled, events);
        using var client = CreateClient(host);
        using var response = await client.GetAsync($"/contact?culture={culture}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        if (mode == ResponseMode.Unexpected && response.StatusCode != HttpStatusCode.InternalServerError)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                boundary = "contact-unexpected-render",
                culture,
                enabled,
                status = (int)response.StatusCode,
                requests = upstream.Paths.Count,
                mediaType = response.Content.Headers.ContentType?.MediaType,
                hasErrorComponent = html.Contains("data-migration-component=\"error-content\"", StringComparison.Ordinal),
                hasContactComponent = html.Contains("data-migration-component=\"contact-form-fields\"", StringComparison.Ordinal),
                hasAntiforgeryText = html.Contains("antiforgery", StringComparison.OrdinalIgnoreCase),
                hasContentTypeText = html.Contains("Content-type", StringComparison.OrdinalIgnoreCase),
                hasIncidentHeader = response.Headers.Contains("X-Incident-Id"),
                hasPrivateDetail = html.Contains("private upstream detail", StringComparison.Ordinal),
                frameworkEvents = events.Snapshot(),
            }));
        }
        Assert.Equal(mode == ResponseMode.Unexpected ? HttpStatusCode.InternalServerError : HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("private upstream detail", html, StringComparison.Ordinal);
        if (mode == ResponseMode.Unexpected)
        {
            Assert.Contains("data-migration-component=\"error-content\"", html, StringComparison.Ordinal);
            // Existing ErrorContent heading key and authoritative Thai resource value.
            Assert.Contains(culture == "th" ? "ขออภัย ระบบทำงานผิดพลาด" : "Sorry. Something did not work properly.", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-migration-component=\"contact-form-fields\"", html, StringComparison.Ordinal);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
            var incident = Assert.Single(response.Headers.GetValues("X-Incident-Id"));
            Assert.True(Guid.TryParseExact(incident, "N", out _));
            Assert.Contains(incident, html, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("data-migration-component=\"contact-form-fields\"", html, StringComparison.Ordinal);
            if (mode is ResponseMode.Transport or ResponseMode.Unavailable or ResponseMode.Missing)
            {
                Assert.Contains(culture == "th" ? "ไม่สามารถดึงรายชื่อประเทศจากเซิร์ฟเวอร์ได้" : CountryError, html, StringComparison.Ordinal);
                Assert.DoesNotContain("<option value=\"Thailand\"", html, StringComparison.Ordinal);
                if (culture == "en")
                {
                    Assert.DoesNotContain(CountryError + ".", html, StringComparison.Ordinal);
                }
            }
            else if (mode == ResponseMode.Success)
            {
                Assert.Contains("Thailand", html, StringComparison.Ordinal);
                Assert.DoesNotContain(CountryError, html, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain(CountryError, html, StringComparison.Ordinal);
                Assert.DoesNotContain("<option value=\"Thailand\"", html, StringComparison.Ordinal);
            }
        }
        Assert.Equal("/Countries", Assert.Single(upstream.Paths));
    }

    [Theory]
    [MemberData(nameof(RouteCases))]
    public async Task CareerRoute_ActualMaxIntHttpPreservesMissingAndFailureContracts(string culture, bool enabled, ResponseMode mode)
    {
        using var upstream = new Upstream(mode, countries: false);
        await using var host = CreateFactory(upstream, "CareerDetail", enabled);
        using var client = CreateClient(host);
        using var response = await client.GetAsync($"/career/view/2147483647?culture={culture}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var expected = mode switch
        {
            ResponseMode.Success => HttpStatusCode.OK,
            ResponseMode.Missing => HttpStatusCode.NotFound,
            ResponseMode.Unexpected => HttpStatusCode.InternalServerError,
            _ => HttpStatusCode.ServiceUnavailable,
        };
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("private upstream detail", html, StringComparison.Ordinal);
        if (mode == ResponseMode.Success)
        {
            Assert.Contains("Manufacturing Engineer", html, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("data-migration-component=\"error-content\"", html, StringComparison.Ordinal);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        }
        Assert.Equal("/Jobs/2147483647", Assert.Single(upstream.Paths));
    }

    private WebApplicationFactory<Program> CreateFactory(Upstream upstream, string route, bool enabled,
        BoundaryLogProvider? events = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"BlazorRouting:{route}", enabled.ToString());
            if (events is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(events)
                    .AddFilter<BoundaryLogProvider>(null, LogLevel.Debug));
            }
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.RemoveAll<ICareerClient>();
                services.RemoveAll<IContactTrustedCustomerLoader>();
                services.AddSingleton<ICountryClient>(upstream.Country);
                services.AddSingleton<ICareerClient>(upstream.Career);
                services.AddSingleton<IContactTrustedCustomerLoader, AnonymousCustomer>();
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> host) => host.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static ContactPage CreateContact(ICountryClient countries) => new(
        countries, null!, null!, null!, Options.Create(new RecaptchaEnterpriseOptions()),
        Options.Create(new GoogleMapsOptions()), new AnonymousCustomer(),
        new StringLocalizer<ContactContent>(new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }), NullLoggerFactory.Instance)),
        NullLogger<ContactPage>.Instance)
    {
        PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
    };

    public enum ResponseMode
    {
        Success,
        EmptySuccess,
        Missing,
        Transport,
        Unavailable,
        Unexpected,
    }

    private sealed class CancelledCountries : ICountryClient
    {
        public int Calls { get; private set; }
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromCanceled<ServiceResponse<IReadOnlyList<Country>>>(cancellationToken);
        }
    }

    private sealed class AnonymousCustomer : IContactTrustedCustomerLoader
    {
        public Task<ContactTrustedCustomerLoadResult> LoadAsync(HttpContext context, IReadOnlyList<Country> countries,
            CancellationToken cancellationToken) => Task.FromResult(new ContactTrustedCustomerLoadResult(null, false, true));
    }

    private sealed class Upstream : IDisposable, IHttpClientFactory
    {
        private readonly HttpClient client;
        public List<string> Paths { get; } = [];
        public CountryClient Country { get; }
        public CareerClient Career { get; }

        public Upstream(ResponseMode mode, bool countries)
        {
            client = new HttpClient(new Handler(request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Null(request.Headers.Authorization);
                var path = request.RequestUri!.AbsolutePath;
                Paths.Add(path);
                if (path == "/jobs/levels")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<CareerLevel>()) };
                }
                Assert.Equal(countries ? "/Countries" : "/Jobs/2147483647", path);
                return mode switch
                {
                    ResponseMode.Transport => throw new HttpRequestException("private upstream detail"),
                    ResponseMode.Unexpected => throw new InvalidOperationException("private upstream detail"),
                    ResponseMode.Missing => new HttpResponseMessage(HttpStatusCode.NotFound),
                    ResponseMode.Unavailable => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                    ResponseMode.EmptySuccess => new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(Array.Empty<Country>()),
                    },
                    _ => new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = countries
                            ? JsonContent.Create(new[] { new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null) })
                            : JsonContent.Create(new CareerOffer(int.MaxValue, 1, "Manufacturing Engineer", null, null,
                                null, null, "Nonthaburi", false, null, null, new CareerLevel(1, "Engineer", null, null, null))),
                    },
                };
            }))
            { BaseAddress = new Uri("https://upstream.invalid/") };
            Country = new CountryClient(this, NullLogger<CountryClient>.Instance);
            Career = new CareerClient(this, NullLogger<CareerClient>.Instance);
        }

        public HttpClient CreateClient(string name)
        {
            Assert.Contains(name, new[] { "countries", "careers" });
            return client;
        }

        public void Dispose() => client.Dispose();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class BoundaryLogProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<BoundaryEvent> events = new();
        private int count;
        public ILogger CreateLogger(string categoryName) => new BoundaryLogger(this, categoryName);
        public BoundaryEvent[] Snapshot() => events.ToArray();
        public void Dispose()
        {
        }

        private sealed class BoundaryLogger(BoundaryLogProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => category.StartsWith("Microsoft.AspNetCore.Components.Endpoints", StringComparison.Ordinal)
                || category.StartsWith("Microsoft.AspNetCore.Diagnostics", StringComparison.Ordinal)
                || category == "Legacy.Maliev.Web.Middleware.ErrorIncidentMiddleware"
                || category == "Maliev.Aspire.ServiceDefaults.Middleware.ExceptionHandlingMiddleware";
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel) && (logLevel >= LogLevel.Warning || exception is not null)
                    && Interlocked.Increment(ref owner.count) <= 64)
                {
                    owner.events.Enqueue(new BoundaryEvent(category, eventId.Id, eventId.Name, exception?.GetType().Name));
                }
            }
        }
    }

    private sealed record BoundaryEvent(string Category, int EventId, string? EventName, string? ExceptionType);

    private sealed record OriginalServiceResponse<T>(T? Value, bool ServiceAvailable) where T : class;
}
