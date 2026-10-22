using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Exercises the Development-only fixture through the real Web cookie and submission boundaries.</summary>
/// <param name="redis">The existing Redis and certificate fixture used by non-Testing Web hosts.</param>
public sealed class DevelopmentCustomerFixtureTests(RedisRuntimeFixture redis)
    : IClassFixture<RedisRuntimeFixture>
{
    /// <summary>Requires the actual Development login to issue only a separate, nonpersistent UI cookie.</summary>
    /// <param name="active">Whether the Blazor or retained Razor login GET owns the route.</param>
    /// <returns>A task representing the actual login and quotation HTTP checks.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Login_UsesIsolatedCookieAndRendersSourceProfile(bool active)
    {
        var effects = new DevelopmentFixtureEffects();
        using var application = DevelopmentFixtureEffects.CreateFactory(redis, effects, active: active);
        using var client = DevelopmentFixtureEffects.Client(application);
        var token = await DevelopmentFixtureEffects.LoginToken(client);
        using var response = await DevelopmentFixtureEffects.SignIn(client, token);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/InstantQuotation/3D-Printing", response.Headers.Location?.OriginalString);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(DevelopmentCustomerFixture.CookieName + "=", StringComparison.Ordinal));
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-Maliev.Legacy.Session=", StringComparison.Ordinal));
        var options = application.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(DevelopmentCustomerFixture.Scheme);
        var ticket = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(cookie.Split(';')[0].Split('=', 2)[1]));
        Assert.NotNull(ticket);
        Assert.Equal(DevelopmentCustomerFixture.Scheme, ticket.AuthenticationScheme);
        Assert.False(ticket.Properties.IsPersistent);
        Assert.Equal("development-customer-fixture", ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.DoesNotContain(ticket.Principal.Claims, claim => claim.Type is
            "identity_kind" or "legacy_database_id" or "legacy_session_id");
        var schemes = application.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme,
            (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/InstantQuotation/3D-Printing?culture=en"));
        Assert.Contains("customer.fixture@localhost", html, StringComparison.Ordinal);
        Assert.Contains("data-development-customer-preview", html, StringComparison.Ordinal);
        Assert.DoesNotContain("We could not securely load your customer profile.", html, StringComparison.Ordinal);
        Assert.Equal(0, effects.ProfileReads);
        Assert.Equal(0, effects.TokenReads);
        Assert.Equal(0, effects.ExternalRequests);
        Assert.True(effects.CountryReads > 0);
        using var member = await client.GetAsync("/Member");
        Assert.Equal(HttpStatusCode.Redirect, member.StatusCode);
        Assert.NotNull(member.Headers.Location);
        Assert.StartsWith("/Account/Login", member.Headers.Location.PathAndQuery, StringComparison.Ordinal);
        using var login = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/InstantQuotation/3D-Printing", login.Headers.Location is { IsAbsoluteUri: true } location
            ? location.AbsolutePath : login.Headers.Location?.OriginalString);
    }

    /// <summary>Requires every non-Development environment to reject direct fixture entry without issuing its cookie.</summary>
    /// <param name="environment">The actual host environment.</param>
    /// <returns>A task representing the unavailable-entry checks.</returns>
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task Entry_IsAbsentAndDirectPostIsNotFoundOutsideDevelopment(string environment)
    {
        var effects = new DevelopmentFixtureEffects();
        using var application = DevelopmentFixtureEffects.CreateFactory(redis, effects, environment);
        using var client = DevelopmentFixtureEffects.Client(application);
        var html = await client.GetStringAsync("/Account/Login");
        Assert.DoesNotContain("data-development-customer-fixture", html, StringComparison.Ordinal);
        var token = DevelopmentFixtureEffects.Token(html);
        using var response = await DevelopmentFixtureEffects.SignIn(client, token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(value => value.StartsWith(DevelopmentCustomerFixture.CookieName + "=", StringComparison.Ordinal)));
        Assert.Null(await application.Services.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(DevelopmentCustomerFixture.Scheme));
        Assert.Equal(0, effects.ExternalRequests);
        Assert.Equal(0, effects.TokenReads);
    }

    /// <summary>Requires real antiforgery validation before the Development cookie can be issued or cleared.</summary>
    /// <returns>A task representing invalid-antiforgery requests.</returns>
    [Fact]
    public async Task FixtureHandlers_RejectMissingAndInvalidAntiforgery()
    {
        var effects = new DevelopmentFixtureEffects();
        using var application = DevelopmentFixtureEffects.CreateFactory(redis, effects);
        using var client = DevelopmentFixtureEffects.Client(application);
        foreach (var handler in new[] { "DevelopmentCustomerFixture", "DevelopmentCustomerFixtureSignOut" })
        {
            using var missing = await client.PostAsync("/Account/Login?handler=" + handler,
                new FormUrlEncodedContent([]));
            using var invalid = await client.PostAsync("/Account/Login?handler=" + handler,
                new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = "invalid" }));
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        Assert.Equal(0, effects.ExternalRequests);
    }

    /// <summary>Requires fixture rejection before any normal preparation, token, submission, or downstream effect.</summary>
    /// <param name="json">Whether the caller requests the JSON submission wire contract explicitly.</param>
    /// <returns>A task representing the real protected POST and repeat-safe rejection.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Submit_AlwaysReturnsRawRetryBeforeEffects(bool json)
    {
        var effects = new DevelopmentFixtureEffects();
        using var application = DevelopmentFixtureEffects.CreateFactory(redis, effects);
        using var client = DevelopmentFixtureEffects.Client(application);
        var token = await DevelopmentFixtureEffects.LoginToken(client);
        using var signedIn = await DevelopmentFixtureEffects.SignIn(client, token);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        token = DevelopmentFixtureEffects.Token(await client.GetStringAsync("/InstantQuotation/3D-Printing"));
        if (json) client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["FirstName"] = "posted replacement",
            ["Email"] = "replacement@example.invalid",
            ["Company"] = "Missing company supplied by the operator",
            ["TaxNumber"] = "",
            ["ShipToBillingAddress"] = "true",
            ["SessionId"] = new string('A', 64),
            ["OwnerIdentity"] = "customer:42",
            ["DevelopmentCustomerFixture"] = "false",
        };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PostAsync("/InstantQuotation/3D-Printing?handler=SubmitRequest",
                new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("retry", body.RootElement.GetProperty("outcome").GetString());
            Assert.Contains(body.RootElement.GetProperty("errors").EnumerateArray(),
                error => error.GetString() == "Development fixture mode stops here; no customer account or quotation was written.");
            Assert.False(body.RootElement.TryGetProperty("redirectUrl", out _));
            Assert.False(body.RootElement.TryGetProperty("requestReference", out _));
        }
        Assert.Equal(0, effects.PreparationCalls);
        Assert.Equal(0, effects.SubmissionCalls);
        Assert.Equal(0, effects.ProfileReads);
        Assert.Equal(0, effects.TokenReads);
        Assert.Equal(0, effects.ExternalRequests);
    }

    /// <summary>Requires sign-out to clear the named fixture cookie and return to the fixed public home.</summary>
    /// <returns>A task representing fixture login, sign-out, and inactive rejection.</returns>
    [Fact]
    public async Task SignOut_ClearsOnlyFixtureAndInactiveRequestIsNotFound()
    {
        var effects = new DevelopmentFixtureEffects();
        using var application = DevelopmentFixtureEffects.CreateFactory(redis, effects);
        using var client = DevelopmentFixtureEffects.Client(application);
        var token = await DevelopmentFixtureEffects.LoginToken(client);
        using var signedIn = await DevelopmentFixtureEffects.SignIn(client, token);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        token = DevelopmentFixtureEffects.Token(await client.GetStringAsync("/InstantQuotation/3D-Printing"));
        using var signedOut = await client.PostAsync("/Account/Login?handler=DevelopmentCustomerFixtureSignOut",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal("/", signedOut.Headers.Location?.OriginalString);
        Assert.DoesNotContain(signedOut.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-Maliev.Legacy.Session=", StringComparison.Ordinal));
        var html = await client.GetStringAsync("/Account/Login");
        Assert.Contains("data-development-customer-fixture", html, StringComparison.Ordinal);
        using var inactive = await client.PostAsync("/Account/Login?handler=DevelopmentCustomerFixtureSignOut",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = DevelopmentFixtureEffects.Token(html),
            }));
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);
        Assert.Equal(0, effects.ExternalRequests);
        Assert.Equal(0, effects.TokenReads);
    }

    /// <summary>Requires a damaged fixture cookie or a query flag to leave the actual UI unauthenticated.</summary>
    /// <returns>A task representing hostile fixture inputs through the real cookie handler.</returns>
    [Fact]
    public async Task TamperedCookieAndQueryFlag_CannotActivateFixture()
    {
        var effects = new DevelopmentFixtureEffects();
        using var application = DevelopmentFixtureEffects.CreateFactory(redis, effects);
        using var client = DevelopmentFixtureEffects.Client(application, cookies: false);
        client.DefaultRequestHeaders.Add("Cookie", DevelopmentCustomerFixture.CookieName + "=untrusted");
        var html = await client.GetStringAsync("/InstantQuotation/3D-Printing?developmentCustomerFixture=true");
        Assert.DoesNotContain("data-development-customer-preview", html, StringComparison.Ordinal);
        Assert.DoesNotContain("customer.fixture@localhost", html, StringComparison.Ordinal);
        Assert.Equal(0, effects.ProfileReads);
        Assert.Equal(0, effects.TokenReads);
    }

    /// <summary>Requires the actual cookie handler to reject expired or over-privileged mutations of a real fixture ticket.</summary>
    /// <param name="expired">Whether the ticket is expired instead of carrying an unexpected authority claim.</param>
    /// <returns>A task representing the hostile-cookie HTTP check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModifiedFixtureTicket_CannotActivateOrAcquireMemberAuthority(bool expired)
    {
        var effects = new DevelopmentFixtureEffects();
        using var application = DevelopmentFixtureEffects.CreateFactory(redis, effects);
        using var issuer = DevelopmentFixtureEffects.Client(application);
        using var signedIn = await DevelopmentFixtureEffects.SignIn(issuer,
            await DevelopmentFixtureEffects.LoginToken(issuer));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        var cookie = Assert.Single(signedIn.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(DevelopmentCustomerFixture.CookieName + "=", StringComparison.Ordinal));
        var options = application.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(DevelopmentCustomerFixture.Scheme);
        var ticket = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(cookie.Split(';')[0].Split('=', 2)[1]));
        Assert.NotNull(ticket);
        if (expired) ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        else Assert.IsType<ClaimsIdentity>(ticket.Principal.Identity).AddClaim(new Claim("legacy_database_id", "42"));
        using var hostile = DevelopmentFixtureEffects.Client(application, cookies: false);
        hostile.DefaultRequestHeaders.Add("Cookie", DevelopmentCustomerFixture.CookieName + "="
            + options.TicketDataFormat.Protect(ticket));
        var html = await hostile.GetStringAsync("/InstantQuotation/3D-Printing");
        Assert.DoesNotContain("data-development-customer-preview", html, StringComparison.Ordinal);
        using var member = await hostile.GetAsync("/Member");
        Assert.Equal(HttpStatusCode.Redirect, member.StatusCode);
        Assert.Equal(0, effects.ProfileReads);
        Assert.Equal(0, effects.TokenReads);
    }

    /// <summary>Requires the fixture profile to restore locked values without discarding editable draft fields.</summary>
    [Fact]
    public void Profile_PreservesExactSourceLocksAndBillingDefault()
    {
        var profile = DevelopmentCustomerFixture.MergeProfile(new InstantQuotationProfileDetails
        {
            FirstName = "replacement",
            Email = "replacement@example.invalid",
            Company = "New company",
            TaxNumber = "0105559123456",
            Telephone = "020000000",
            ShippingStreet1 = "Separate delivery",
            ShipToBillingAddress = false,
        });
        Assert.Equal("ทดสอบ", profile.FirstName);
        Assert.Equal("ลูกค้า", profile.LastName);
        Assert.Equal("customer.fixture@localhost", profile.Email);
        Assert.Equal("0890000000", profile.Mobile);
        Assert.Equal("อาคารทดสอบ", profile.BillingBuilding);
        Assert.Equal("99 ถนนทดสอบ", profile.BillingStreet1);
        Assert.Equal("เมืองนนทบุรี", profile.BillingCity);
        Assert.Equal("นนทบุรี", profile.BillingProvince);
        Assert.Equal("11000", profile.BillingPostalCode);
        Assert.Equal("Thailand", profile.Country);
        Assert.Equal("New company", profile.Company);
        Assert.Equal("0105559123456", profile.TaxNumber);
        Assert.Equal("Separate delivery", profile.ShippingStreet1);
        Assert.False(profile.ShipToBillingAddress);
        var model = DevelopmentCustomerFixture.ApplyProfile(
            Legacy.Maliev.Web.Components.Pages.InstantQuotation.InstantQuotationCustomerDisplayModel.Empty);
        Assert.True(model.ShipToBillingAddress);
        Assert.True(model.IsDevelopmentCustomerFixture);
        Assert.Equal(new[]
        {
            "BillingBuilding", "BillingCity", "BillingPostalCode", "BillingProvince",
            "BillingStreet1", "Country", "Email", "FirstName", "LastName", "Mobile",
        }, model.LockedFields.Order(StringComparer.Ordinal));
        Assert.False(model.IsLocked("Company"));
        Assert.False(model.IsLocked("TaxNumber"));
        Assert.False(model.IsLocked("ShippingStreet1"));
        Assert.False(model.IsLocked("ShipToBillingAddress"));
    }

    /// <summary>Requires test-host disposal to release its created Redis connection while retaining a later borrowed replacement.</summary>
    /// <returns>A task representing the actual two-host ownership check.</returns>
    [Fact]
    public async Task Factory_DisposesCreatedRedisButNotLaterBorrowedReplacement()
    {
        using var owner = redis.CreateFactory();
        var borrowed = owner.Services.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>();
        StackExchange.Redis.IConnectionMultiplexer? created = null;
        await using var application = redis.CreateFactory("Development", builder => builder.ConfigureTestServices(services =>
        {
            created = Assert.IsAssignableFrom<StackExchange.Redis.IConnectionMultiplexer>(services.Single(
                service => service.ServiceType == typeof(StackExchange.Redis.IConnectionMultiplexer)).ImplementationInstance);
            services.RemoveAll<StackExchange.Redis.IConnectionMultiplexer>();
            services.AddSingleton(borrowed);
        }));
        Assert.Same(borrowed, application.Services.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>());
        Assert.NotNull(created);
        Assert.NotSame(created, borrowed);
        await application.DisposeAsync();
        Assert.False(created.IsConnected);
        Assert.True(borrowed.IsConnected);
        Assert.True(await borrowed.GetDatabase().PingAsync() >= TimeSpan.Zero);
    }

    /// <summary>Requires a failure after Redis creation to retain the startup cause and release the captured connection.</summary>
    /// <returns>A task representing failed host construction and owned cleanup.</returns>
    [Fact]
    public async Task Factory_FailedStartRetainsCauseAndReleasesCreatedRedis()
    {
        var primary = new InvalidOperationException("Controlled fixture startup failure.");
        StackExchange.Redis.IConnectionMultiplexer? created = null;
        await using var application = redis.CreateFactory("Development", builder => builder.ConfigureTestServices(services =>
        {
            created = Assert.IsAssignableFrom<StackExchange.Redis.IConnectionMultiplexer>(services.Single(
                service => service.ServiceType == typeof(StackExchange.Redis.IConnectionMultiplexer)).ImplementationInstance);
            throw primary;
        }));
        var failure = Record.Exception(application.StartServer);
        Assert.NotNull(failure);
        Assert.True(ContainsPrimary(failure));
        Assert.NotNull(created);
        await application.DisposeAsync();
        Assert.False(created.IsConnected);

        bool ContainsPrimary(Exception exception) => ReferenceEquals(primary, exception)
            || (exception.InnerException is not null && ContainsPrimary(exception.InnerException))
            || (exception is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsPrimary));
    }

    /// <summary>Leaves unmarked bare controller contexts inactive without consulting missing request services or changing their state.</summary>
    /// <param name="authenticated">Whether the ordinary principal has an authenticated identity.</param>
    /// <param name="owner">The ordinary owner claim, including missing and blank fail-closed inputs.</param>
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "customer:42")]
    public void IsActive_UnmarkedBareContextDoesNotResolveServicesOrMutateItems(bool authenticated, string? owner)
    {
        Claim[] claims = owner is null ? [] : [new Claim(ClaimTypes.NameIdentifier, owner)];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims,
            authenticated ? CookieAuthenticationDefaults.AuthenticationScheme : null));
        var context = new DefaultHttpContext { User = principal };
        var key = new object();
        var value = new object();
        context.Items[key] = value;

        Assert.False(DevelopmentCustomerFixture.IsActive(context));

        Assert.Same(principal, context.User);
        var item = Assert.Single(context.Items);
        Assert.Same(key, item.Key);
        Assert.Same(value, item.Value);
    }

    /// <summary>Requires a genuine default principal to take precedence over a previously authenticated local UI fixture.</summary>
    /// <returns>A task representing the real middleware's principal precedence.</returns>
    [Fact]
    public async Task Middleware_DoesNotReplaceOrFenceAuthenticatedDefaultPrincipal()
    {
        using var application = DevelopmentFixtureEffects.CreateFactory(redis, new DevelopmentFixtureEffects());
        using var client = DevelopmentFixtureEffects.Client(application);
        using var signedIn = await DevelopmentFixtureEffects.SignIn(client,
            await DevelopmentFixtureEffects.LoginToken(client));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        var fixtureCookie = Assert.Single(signedIn.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(DevelopmentCustomerFixture.CookieName + "=", StringComparison.Ordinal)).Split(';')[0];
        using var scope = application.Services.CreateScope();
        var original = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "customer:42")], CookieAuthenticationDefaults.AuthenticationScheme));
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = original };
        context.Request.Scheme = "https";
        context.Request.Headers.Cookie = fixtureCookie;
        var invoked = false;
        await DevelopmentCustomerFixture.ObserveAsync(context, () =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        Assert.True(invoked);
        Assert.Same(original, context.User);
        Assert.False(DevelopmentCustomerFixture.IsActive(context));
        Assert.False(await DevelopmentCustomerFixture.TrySignInAsync(context));
    }
}

internal sealed class DevelopmentFixtureEffects : ICountryClient, IInstantQuotationProfileCompletionClient,
    IInstantQuotationAuthenticatedPreparationService, IInstantQuotationSubmissionService, IServiceAccessTokenProvider
{
    internal int CountryReads;
    internal int ProfileReads;
    internal int PreparationCalls;
    internal int SubmissionCalls;
    internal int TokenReads;
    internal int ExternalRequests;

    internal static WebApplicationFactory<Program> CreateFactory(
        RedisRuntimeFixture redis, DevelopmentFixtureEffects effects,
        string environment = "Development", bool active = true) =>
        redis.CreateFactory(environment, builder =>
        {
            builder.UseSetting("BlazorRouting:Login", active.ToString());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient>(effects);
                services.RemoveAll<IInstantQuotationProfileCompletionClient>();
                services.AddSingleton<IInstantQuotationProfileCompletionClient>(effects);
                services.RemoveAll<IInstantQuotationAuthenticatedPreparationService>();
                services.AddSingleton<IInstantQuotationAuthenticatedPreparationService>(effects);
                services.RemoveAll<IInstantQuotationSubmissionService>();
                services.AddSingleton<IInstantQuotationSubmissionService>(effects);
                services.RemoveAll<IServiceAccessTokenProvider>();
                services.AddSingleton<IServiceAccessTokenProvider>(effects);
                services.ConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                        handlerBuilder.PrimaryHandler = new RejectExternalRequests(effects)));
            });
        });

    internal static HttpClient Client(WebApplicationFactory<Program> application, bool cookies = true) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = cookies,
        });

    internal static async Task<string> LoginToken(HttpClient client) =>
        Token(await client.GetStringAsync("/Account/Login"));

    internal static string Token(string html)
    {
        var tag = Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase).Cast<Match>()
            .First(match => Regex.IsMatch(match.Value, "name=[\"']__RequestVerificationToken[\"']", RegexOptions.IgnoreCase));
        return WebUtility.HtmlDecode(Regex.Match(tag.Value, "value=[\"'](?<value>[^\"']*)[\"']").Groups["value"].Value);
    }

    internal static Task<HttpResponseMessage> SignIn(HttpClient client, string token) =>
        client.PostAsync("/Account/Login?handler=DevelopmentCustomerFixture",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

    public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CountryReads);
        return Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
            [new Country(1, "Thailand", "Asia", "TH", "TH", "THA", null, null)], true));
    }

    public Task<InstantQuotationProfileGraphResult> ReadAsync(string ownerIdentity, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref ProfileReads);
        return Task.FromResult(new InstantQuotationProfileGraphResult(null, InstantQuotationProblemCategory.DependencyUnavailable));
    }

    public Task<InstantQuotationProfileCompletionResult> CompleteAsync(
        string ownerIdentity, InstantQuotationProfileCompletionOperation operation, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Fixture attempted commercial profile completion.");

    public Task<InstantQuotationAuthenticatedPreparation> PrepareAsync(string sessionId, string ownerIdentity,
        InstantQuotationProfileDetails posted, string? description, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref PreparationCalls);
        return Task.FromResult(new InstantQuotationAuthenticatedPreparation(null, null, null,
            InstantQuotationProblemCategory.Authorization, true));
    }

    public Task<InstantQuotationSubmissionResult> SubmitAsync(string sessionId, string? ownerIdentity,
        InstantQuotationCustomerSubmission customer, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref SubmissionCalls);
        throw new InvalidOperationException("Fixture attempted commercial submission.");
    }

    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref TokenReads);
        throw new InvalidOperationException("Fixture attempted service authentication.");
    }

    public void Invalidate(string token) => throw new InvalidOperationException("Fixture invalidated a service token.");

    private sealed class RejectExternalRequests(DevelopmentFixtureEffects effects) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref effects.ExternalRequests);
            throw new InvalidOperationException("Fixture attempted an external HTTP request.");
        }
    }
}
