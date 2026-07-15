using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using System.Globalization;

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
builder.Services.AddHttpContextAccessor();
builder.Services.AddResponseCompression();
builder.Services.AddOutputCache();
builder.Services.AddLegacyServiceClients(builder.Configuration);

var app = builder.Build();
app.UseStandardMiddleware();
app.UseResponseCompression();
app.UseStaticFiles();
app.UseCookiePolicy();
app.UseRequestLocalization();
app.UseMiddleware<CanonicalUrlRedirectMiddleware>();
app.UseRouting();
app.UseCors();
app.UseOutputCache();
app.MapDefaultEndpoints("web");
app.MapRazorPages();
app.MapApiDocumentation(servicePrefix: "web");
await app.RunAsync();

public partial class Program;
