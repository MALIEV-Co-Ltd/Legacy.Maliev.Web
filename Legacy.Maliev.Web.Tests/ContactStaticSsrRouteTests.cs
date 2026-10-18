using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Legacy.Maliev.Web.Tests;

public sealed class ContactStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ContactStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Host_DeclaresContactGetAndRetainsTheServerPostBoundary()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var route = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Contact", "ContactPage.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Contact", "Index.cshtml"));

        Assert.Contains("BlazorRouting:Contact", program, StringComparison.Ordinal);
        Assert.Contains("\"/Contact/Index\"", program, StringComparison.Ordinal);
        Assert.Contains("\"Contact\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ContactFormFields)\"", fallback, StringComparison.Ordinal);
        Assert.Contains("ICountryClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("IContactClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("INotificationClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("IAntiBotVerifier", route, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledContactRoute_UsesTheRetainedRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Contact", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
            });
        });
        using var client = fallbackFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var source = await client.GetStringAsync("/contact?culture=en");

        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountryTransportFailure_KeepsContactGetAvailableWithoutDownstreamWrites()
    {
        var requests = new ConcurrentQueue<(string Client, string Method, string Path)>();
        await using var unavailableFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Services:Country", "https://country-outage.invalid/");
            builder.ConfigureServices(services =>
            {
                foreach (var name in new[] { "countries", "contacts", "notifications", "auth" })
                {
                    services.AddHttpClient(name)
                        .ConfigurePrimaryHttpMessageHandler(() => new CountryOutageTransport(name, requests));
                }

                // Retain the real retry/deadline policy, shortening only test retry scheduling.
                services.PostConfigure<HttpStandardResilienceOptions>("countries-standard", options =>
                {
                    options.Retry.Delay = TimeSpan.Zero;
                    options.Retry.UseJitter = false;
                    options.Retry.DelayGenerator = _ => ValueTask.FromResult<TimeSpan?>(TimeSpan.Zero);
                });
            });
        });
        using var client = unavailableFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var response = await client.GetAsync("/contact?culture=en", deadline.Token);
        var source = await response.Content.ReadAsStringAsync(deadline.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        var form = Regex.Match(source, "<form\\b[^>]*\\bid=\"contact-us\"[^>]*>");
        Assert.True(form.Success);
        Assert.Contains("method=\"post\"", form.Value, StringComparison.Ordinal);
        Assert.Contains("Could not retrieve countries from the server.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("controlled-country-private-failure", source, StringComparison.Ordinal);
        var select = Regex.Match(source, "<select\\b(?<attributes>[^>]*\\bid=\"Country\"[^>]*)>(?<options>.*?)</select>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(select.Success);
        Assert.Matches("\\sdisabled(?:\\s|=|$)", select.Groups["attributes"].Value);
        Assert.Single(Regex.Matches(select.Groups["options"].Value, "<option\\b", RegexOptions.IgnoreCase).Cast<Match>());
        Assert.Contains("value=\"\"", select.Groups["options"].Value, StringComparison.Ordinal);
        Assert.Contains("Select a country", select.Groups["options"].Value, StringComparison.Ordinal);
        var submit = Regex.Match(source, "<button\\b[^>]*\\bid=\"submit-button\"[^>]*>",
            RegexOptions.IgnoreCase);
        Assert.True(submit.Success);
        Assert.Matches("\\sdisabled(?:\\s|=|>)", submit.Value);
        Assert.NotEmpty(requests);
        Assert.All(requests, request =>
        {
            Assert.Equal("countries", request.Client);
            Assert.Equal("GET", request.Method);
            Assert.Equal("/Countries", request.Path);
        });
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

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)],
                true));
    }

    private sealed class CountryOutageTransport(
        string clientName,
        ConcurrentQueue<(string Client, string Method, string Path)> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requests.Enqueue((clientName, request.Method.Method, request.RequestUri!.AbsolutePath));
            return Task.FromException<HttpResponseMessage>(
                new HttpRequestException("controlled-country-private-failure"));
        }
    }
}
