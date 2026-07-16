using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web;
using Legacy.Maliev.Web.Components;
using Legacy.Maliev.Web.Middleware;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using StackExchange.Redis;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddStandardCors();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.AddStandardOpenApi(
    title: "Legacy MALIEV Web BFF",
    description: "Server-rendered compatibility frontend and BFF for the independently deployable legacy services.");
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { new CultureInfo("th"), new CultureInfo("en") };
    options.DefaultRequestCulture = new RequestCulture("th");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    options.RequestCultureProviders =
    [
        new QueryStringRequestCultureProvider(),
        new CookieRequestCultureProvider()
    ];
});
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = static _ => true;
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure = CookieSecurePolicy.Always;
});
builder.Services.AddRazorPages()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization();
builder.Services.AddRazorComponents();
builder.Services.AddHttpContextAccessor();
builder.Services.AddResponseCompression();
builder.Services.AddOutputCache();
builder.Services.Configure<CookieTempDataProviderOptions>(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "__Host-Maliev.Legacy.TempData";
    options.Cookie.Path = "/";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.AddLegacyServiceClients(builder.Configuration);
builder.Services.AddLegacyAccountAuthentication();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        static _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = 30,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1),
        }));
});

var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("Legacy.Maliev.Web");
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    var redisConnectionString = builder.Configuration.GetConnectionString("redis");
    if (string.IsNullOrWhiteSpace(redisConnectionString))
    {
        throw new InvalidOperationException(
            "Redis connection string 'redis' is required for encrypted customer sessions.");
    }

    var certificatePfxBase64 = builder.Configuration["DataProtection:CertificatePfxBase64"];
    var certificatePassword = builder.Configuration["DataProtection:CertificatePassword"];
    if (string.IsNullOrWhiteSpace(certificatePfxBase64)
        || string.IsNullOrWhiteSpace(certificatePassword))
    {
        throw new InvalidOperationException(
            "A data-protection certificate is required to encrypt the Redis key ring.");
    }

    var keyEncryptionCertificate = X509CertificateLoader.LoadPkcs12(
        Convert.FromBase64String(certificatePfxBase64),
        certificatePassword,
        X509KeyStorageFlags.EphemeralKeySet);
    builder.Services.AddSingleton(keyEncryptionCertificate);
    dataProtection.ProtectKeysWithCertificate(keyEncryptionCertificate);

    var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
    redisOptions.AbortOnConnectFail = false;
    redisOptions.ConnectRetry = 5;
    redisOptions.ConnectTimeout = 10_000;
    redisOptions.AsyncTimeout = 10_000;
    redisOptions.SyncTimeout = 10_000;
    var redis = ConnectionMultiplexer.Connect(redisOptions);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis);
        options.InstanceName = "legacy:web:";
    });
    dataProtection.PersistKeysToStackExchangeRedis(redis, "legacy:web:data-protection-keys");
    builder.Services.AddHealthChecks().AddRedis(
        redisConnectionString,
        name: "redis-sessions",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(10));
}

var app = builder.Build();
app.UseStandardMiddleware();
app.UseMiddleware<WebContentSecurityPolicyMiddleware>();
app.UseExceptionHandler("/Error");
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
app.UseResponseCompression();
app.UseStaticFiles();
app.UseCookiePolicy();
app.UseRequestLocalization();
app.UseMiddleware<CanonicalUrlRedirectMiddleware>();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();
app.MapDefaultEndpoints("web");
app.MapLegacySitemap();
app.MapMemberCompatibilityEndpoints();
app.MapRazorComponents<App>();
app.MapRazorPages();
app.MapApiDocumentation(servicePrefix: "web");
await app.RunAsync();

public partial class Program;
