using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Legacy.Maliev.Web;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Components.Pages.Member;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Real mapped BFF HTTP tests; synthetic boundary fixtures are not joined Auth/registry evidence.</summary>
public sealed class MemberDocumentsHttpTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task RenderedVersionSeparatorHasExplicitUtf8AndNoMojibake(string culture)
    {
        await using var app = await HostAsync(new RecordingRegistry());
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Member", "yes");
        using var response = await http.GetAsync("/fixture/page?culture=" + culture); response.EnsureSuccessStatusCode();
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        var text = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("1 - ", text); Assert.DoesNotContain("\u00e2\u20ac\u201d", text);
    }
    [Fact]
    public async Task CustomerQueryCannotChangeServerSessionScope()
    {
        var registry = new RecordingRegistry(); await using var app = await HostAsync(registry);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Member", "yes");
        using var response = await http.GetAsync("/member/documents?customerId=999");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(42, registry.CustomerId);
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("member-secret", body); Assert.DoesNotContain("accessToken", body);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }
    [Fact]
    public async Task InternalMetadataAndCrossTenantResponsesAreRefusedWithoutPartialDisclosure()
    {
        foreach (var summary in new[] { new CustomerDocumentSummary(Guid.NewGuid(), 42, "Nda", "private-synthetic-title", "Internal", 1), new CustomerDocumentSummary(Guid.NewGuid(), 999, "Nda", "private-synthetic-title", "Customer", 1) })
        {
            var registry = new RecordingRegistry { Summary = summary }; await using var app = await HostAsync(registry);
            using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Member", "yes");
            using var response = await http.GetAsync("/member/documents"); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.DoesNotContain("private-synthetic-title", await response.Content.ReadAsStringAsync());
        }
    }
    [Fact]
    public async Task UnavailableWithStaleTypedPayloadDoesNotDiscloseMetadata()
    {
        var registry = new RecordingRegistry { StatusCode = 503, Summary = new(Guid.NewGuid(), 42, "Nda", "stale-protected-title", "Customer", 1) };
        await using var app = await HostAsync(registry); using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Member", "yes");
        using var response = await http.GetAsync("/member/documents"); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.DoesNotContain("stale-protected-title", await response.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ActualRenderedMemberPageDoesNotRenderStalePayloadOnUnavailable(string culture)
    {
        var registry = new RecordingRegistry { StatusCode = 503, Summary = new(Guid.NewGuid(), 42, "Nda", "stale-protected-title", "Customer", 1) };
        await using var app = await HostAsync(registry); using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Member", "yes");
        using var response = await http.GetAsync("/fixture/page?culture=" + culture); response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("stale-protected-title", html); Assert.Contains("role=\"alert\"", html); Assert.Contains("disabled", html);
        Assert.Contains(culture == "th" ? "เอกสาร" : "Documents", WebUtility.HtmlDecode(html));
    }
    [Theory]
    [InlineData("0", "PendingVerification", true)]
    [InlineData("Nda", "0", true)]
    [InlineData("Nda", "Verified", false)]
    public void VersionContractRejectsUndefinedNamedValuesAndMissingVerifiedEvidence(string kind, string verification, bool timestamp)
    {
        var id = Guid.NewGuid(); var value = new CustomerDocumentVersionSummary(id, Guid.NewGuid(), 1, kind, new string('a', 64), DateTimeOffset.UtcNow, verification, "synthetic-staff", timestamp ? DateTimeOffset.UtcNow : null, 1);
        Assert.False(CustomerDocumentContractGuard.Version(value, id, "Nda"));
    }
    [Fact]
    public async Task UploadWithoutCsrfIsRefusedBeforeRegistry()
    {
        var registry = new RecordingRegistry(); await using var app = await HostAsync(registry);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Member", "yes");
        using var form = new MultipartFormDataContent(); form.Add(new StringContent("Nda"), "Kind");
        using var response = await http.PostAsync("/member/documents/upload", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, registry.UploadCalls);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(20 * 1024 * 1024 + 1)]
    public async Task ActualMultipartRejectsEmptyOrOversizedFileWithValidCsrf(int length)
    {
        var registry = new RecordingRegistry(); await using var app = await HostAsync(registry);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Member", "yes");
        using var csrf = await http.GetAsync("/fixture/csrf");
        var requestToken = await csrf.Content.ReadFromJsonAsync<string>();
        http.DefaultRequestHeaders.Add("Cookie", csrf.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        http.DefaultRequestHeaders.Add("RequestVerificationToken", requestToken);
        using var form = new MultipartFormDataContent(); form.Add(new StringContent("Nda"), "Kind");
        using var file = new ByteArrayContent(new byte[length]); file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf"); form.Add(file, "files", "synthetic.pdf");
        using var response = await http.PostAsync("/member/documents/upload", form);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode); Assert.Equal(0, registry.UploadCalls);
    }
    [Fact]
    public async Task AnonymousReadIsRefusedBeforeRegistry()
    {
        var registry = new RecordingRegistry(); await using var app = await HostAsync(registry);
        using var http = app.GetTestClient(); using var response = await http.GetAsync("/member/documents");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.Equal(0, registry.CustomerId);
    }
    [Theory]
    [InlineData(42, 302)]
    [InlineData(999, 503)]
    public async Task ActualMultipartAcceptsOnlyCanonicalTypedAcknowledgement(int acknowledgedCustomer, int expectedStatus)
    {
        var registry = new RecordingRegistry { UploadReceipt = new CustomerDocumentVersionReceipt(Guid.NewGuid(), Guid.NewGuid(), acknowledgedCustomer, 1, new string('a', 64), 1) };
        await using var app = await HostAsync(registry); using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Member", "yes");
        using var csrf = await http.GetAsync("/fixture/csrf");
        http.DefaultRequestHeaders.Add("Cookie", csrf.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        http.DefaultRequestHeaders.Add("RequestVerificationToken", await csrf.Content.ReadFromJsonAsync<string>());
        using var form = new MultipartFormDataContent(); form.Add(new StringContent("Corporate"), "Kind"); form.Add(new StringContent("synthetic-title"), "Title"); form.Add(new StringContent(Guid.NewGuid().ToString("D")), "IdempotencyKey");
        using var file = new ByteArrayContent([1]); file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf"); form.Add(file, "files", "synthetic.pdf");
        using var response = await http.PostAsync("/member/documents/upload?customerId=999", form);
        Assert.Equal(expectedStatus, (int)response.StatusCode); Assert.Equal(1, registry.UploadCalls); Assert.Equal(42, registry.CustomerId);
        if (expectedStatus == 302) Assert.Equal("/Member/Documents?uploaded=true", response.Headers.Location!.OriginalString);
        else Assert.Null(response.Headers.Location);
    }
    public static async Task<WebApplication> HostAsync(RecordingRegistry registry, bool loopbackBrowser = false)
    {
        var builder = WebApplication.CreateBuilder();
        if (loopbackBrowser) builder.WebHost.UseUrls("http://127.0.0.1:0"); else builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders(); builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Configuration["CustomerDocuments:Enabled"] = "true";
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("fixture", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddAntiforgery(); builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization(options => options.ResourcesPath = "Resources"); builder.Services.AddSingleton<IAccountSessionManager>(new FixtureSession()); builder.Services.AddSingleton<ICustomerDocumentClient>(registry);
        var app = builder.Build(); app.UseRequestLocalization(new Microsoft.AspNetCore.Builder.RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en", "th").AddSupportedUICultures("en", "th")); app.UseAuthentication(); app.UseAuthorization(); app.MapCustomerDocumentEndpoints();
        app.MapGet("/fixture/csrf", (HttpContext context, IAntiforgery antiforgery) => Results.Json(antiforgery.GetAndStoreTokens(context).RequestToken));
        app.MapGet("/fixture/page", async (HttpContext context) =>
        {
            await using var renderer = new HtmlRenderer(context.RequestServices, context.RequestServices.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<MemberDocumentsPage>(ParameterView.Empty)).ToHtmlString());
            return Results.Content(html, "text/html; charset=utf-8");
        }).RequireAuthorization();
        await app.StartAsync(); return app;
    }
    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.ContainsKey("Synthetic-Member") ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "synthetic-member"), new Claim("identity_kind", "customer")], Scheme.Name)), Scheme.Name)) : AuthenticateResult.NoResult());
    }
    private sealed class FixtureSession : IAccountSessionManager
    {
        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken token) => Task.FromResult<string?>("member-secret");
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken token) => Task.FromResult<int?>(42);
        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken token) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, CancellationToken token) => throw new NotSupportedException();
    }
    public sealed class RecordingRegistry : ICustomerDocumentClient
    {
        public int CustomerId { get; private set; }
        public int UploadCalls { get; private set; }
        public int StatusCode { get; init; } = 200;
        public CustomerDocumentVersionReceipt? UploadReceipt { get; init; }
        public CustomerDocumentReceipt? ReceiptValue { get; init; }
        public IReadOnlyList<CustomerDocumentVersionSummary>? VersionValues { get; init; }
        public CustomerDocumentSummary Summary { get; init; } = new(Guid.NewGuid(), 42, "Nda", "synthetic-title", "Customer", 1);
        public Task<CustomerDocumentResult<IReadOnlyList<CustomerDocumentSummary>>> ListAsync(int customerId, string accessToken, CancellationToken token) { CustomerId = customerId; Assert.Equal("member-secret", accessToken); return Task.FromResult(new CustomerDocumentResult<IReadOnlyList<CustomerDocumentSummary>>(StatusCode, [Summary])); }
        public Task<CustomerDocumentResult<IReadOnlyList<CustomerDocumentVersionSummary>>> VersionsAsync(int customerId, Guid documentId, string accessToken, CancellationToken token) => Task.FromResult(new CustomerDocumentResult<IReadOnlyList<CustomerDocumentVersionSummary>>(200, VersionValues ?? [new(documentId, Guid.NewGuid(), 1, Summary.Kind, new string('a', 64), DateTimeOffset.UtcNow, "PendingVerification", null, null, 1)]));
        public Task<CustomerDocumentResult<CustomerDocumentReceipt>> ReceiptAsync(int customerId, Guid documentId, Guid versionId, string accessToken, CancellationToken token) { CustomerId = customerId; Assert.Equal("member-secret", accessToken); return Task.FromResult(new CustomerDocumentResult<CustomerDocumentReceipt>(200, ReceiptValue)); }
        public Task<CustomerDocumentResult<byte[]>> DownloadAsync(int customerId, Guid documentId, Guid versionId, string accessToken, CancellationToken token) => throw new NotSupportedException();
        public Task<CustomerDocumentResult<CustomerDocumentVersionReceipt>> UploadAsync(int customerId, Guid? documentId, long? expectedRevision, string kind, string title, Stream content, string fileName, string contentType, string idempotencyKey, string accessToken, CancellationToken token) { UploadCalls++; CustomerId = customerId; Assert.Equal("member-secret", accessToken); return Task.FromResult(new CustomerDocumentResult<CustomerDocumentVersionReceipt>(UploadReceipt is null ? 503 : 200, UploadReceipt)); }
    }
}
