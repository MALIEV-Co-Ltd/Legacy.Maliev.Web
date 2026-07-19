using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Legacy.Maliev.Web.Middleware;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Routing;
using StackExchange.Redis;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var useBlazorHomeRoute = builder.Configuration.GetValue("BlazorRouting:Home", true);
var useBlazorAboutRoute = builder.Configuration.GetValue("BlazorRouting:About", true);
var useBlazorSocialMediaRoute = builder.Configuration.GetValue("BlazorRouting:SocialMedia", true);
var useBlazorLegalRoute = builder.Configuration.GetValue("BlazorRouting:Legal", true);
var useBlazorNonDisclosureAgreementRoute = builder.Configuration.GetValue("BlazorRouting:NonDisclosureAgreement", true);
var useBlazorAccessDeniedRoute = builder.Configuration.GetValue("BlazorRouting:AccessDenied", true);
var useBlazorErrorRoute = builder.Configuration.GetValue("BlazorRouting:Error", true);
var useBlazorAccountIndexRoute = builder.Configuration.GetValue("BlazorRouting:AccountIndex", true);
var useBlazorLoginRoute = builder.Configuration.GetValue("BlazorRouting:Login", true);
var useBlazorSignupRoute = builder.Configuration.GetValue("BlazorRouting:Signup", true);
var useBlazorForgotPasswordRoute = builder.Configuration.GetValue("BlazorRouting:ForgotPassword", true);
var useBlazorResetPasswordRoute = builder.Configuration.GetValue("BlazorRouting:ResetPassword", true);
var useBlazorLogoutRoute = builder.Configuration.GetValue("BlazorRouting:Logout", true);
var useBlazorEmailConfirmationRoute = builder.Configuration.GetValue("BlazorRouting:EmailConfirmation", true);
var useBlazorChangeEmailConfirmationRoute = builder.Configuration.GetValue("BlazorRouting:ChangeEmailConfirmation", true);
var useBlazorContactRoute = builder.Configuration.GetValue("BlazorRouting:Contact", true);
var useBlazorQuotationRoute = builder.Configuration.GetValue("BlazorRouting:Quotation", true);
var useBlazorInstantQuotationRoute = builder.Configuration.GetValue("BlazorRouting:InstantQuotation", true);
var useBlazorMemberOverviewRoute = builder.Configuration.GetValue("BlazorRouting:MemberOverview", true);
var useBlazorMemberAccountIndexRoute = builder.Configuration.GetValue("BlazorRouting:MemberAccountIndex", true);
var useBlazorMemberOrdersIndexRoute = builder.Configuration.GetValue("BlazorRouting:MemberOrdersIndex", true);
var useBlazorMemberOrderHistoryRoute = builder.Configuration.GetValue("BlazorRouting:MemberOrderHistory", true);
var useBlazorMemberQuotationsIndexRoute = builder.Configuration.GetValue("BlazorRouting:MemberQuotationsIndex", true);
var useBlazorMemberOrderDetailRoute = builder.Configuration.GetValue("BlazorRouting:MemberOrderDetail", true);
var useBlazorMemberQuotationDetailRoute = builder.Configuration.GetValue("BlazorRouting:MemberQuotationDetail", true);
var useBlazorMemberProfileRoute = builder.Configuration.GetValue("BlazorRouting:MemberProfile", true);
var useBlazorMemberAddressRoute = builder.Configuration.GetValue("BlazorRouting:MemberAddress", true);
var useBlazorMemberChangeEmailRoute = builder.Configuration.GetValue("BlazorRouting:MemberChangeEmail", true);
var useBlazorMemberChangePasswordRoute = builder.Configuration.GetValue("BlazorRouting:MemberChangePassword", true);
var useBlazorPrivacyPolicyRoute = builder.Configuration.GetValue("BlazorRouting:PrivacyPolicy", true);
var useBlazorTermsConditionsRoute = builder.Configuration.GetValue("BlazorRouting:TermsConditions", true);
var useBlazorCareerIndexRoute = builder.Configuration.GetValue("BlazorRouting:CareerIndex", true);
var useBlazorCareerDetailRoute = builder.Configuration.GetValue("BlazorRouting:CareerDetail", true);
var useBlazorServicesRoute = builder.Configuration.GetValue("BlazorRouting:Services", true);
var useBlazorKnowledgesIndexRoute = builder.Configuration.GetValue("BlazorRouting:KnowledgesIndex", true);
var useBlazorKnowledgesWorkflowRoute = builder.Configuration.GetValue("BlazorRouting:KnowledgesWorkflow", true);
var useBlazorKnowledgesGuidelinesRoute = builder.Configuration.GetValue("BlazorRouting:KnowledgesGuidelines", true);
var useBlazorKnowledgesSpecificationsRoute = builder.Configuration.GetValue("BlazorRouting:KnowledgesSpecifications", true);
var useBlazorKnowledgesSpecifications3DPrintingRoute = builder.Configuration.GetValue("BlazorRouting:KnowledgesSpecifications3DPrinting", true);
var useBlazorKnowledgesSpecifications3DScanningRoute = builder.Configuration.GetValue("BlazorRouting:KnowledgesSpecifications3DScanning", true);
var useBlazorKnowledgesSpecificationsCncMachiningRoute = builder.Configuration.GetValue("BlazorRouting:KnowledgesSpecificationsCncMachining", true);
var useBlazorRouteHost = useBlazorHomeRoute
    && useBlazorAboutRoute
    && useBlazorSocialMediaRoute
    && useBlazorLegalRoute
    && useBlazorNonDisclosureAgreementRoute
    && useBlazorAccessDeniedRoute
    && useBlazorErrorRoute
    && useBlazorAccountIndexRoute
    && useBlazorLoginRoute
    && useBlazorSignupRoute
    && useBlazorForgotPasswordRoute
    && useBlazorResetPasswordRoute
    && useBlazorLogoutRoute
    && useBlazorEmailConfirmationRoute
    && useBlazorChangeEmailConfirmationRoute
    && useBlazorContactRoute
    && useBlazorQuotationRoute
    && useBlazorInstantQuotationRoute
    && useBlazorMemberOverviewRoute
    && useBlazorMemberAccountIndexRoute
    && useBlazorMemberOrdersIndexRoute
    && useBlazorMemberOrderHistoryRoute
    && useBlazorMemberQuotationsIndexRoute
    && useBlazorMemberOrderDetailRoute
    && useBlazorMemberQuotationDetailRoute
    && useBlazorMemberProfileRoute
    && useBlazorMemberAddressRoute
    && useBlazorMemberChangeEmailRoute
    && useBlazorMemberChangePasswordRoute
    && useBlazorPrivacyPolicyRoute
    && useBlazorTermsConditionsRoute
    && useBlazorCareerIndexRoute
    && useBlazorCareerDetailRoute
    && useBlazorServicesRoute
    && useBlazorKnowledgesIndexRoute
    && useBlazorKnowledgesWorkflowRoute
    && useBlazorKnowledgesGuidelinesRoute
    && useBlazorKnowledgesSpecificationsRoute
    && useBlazorKnowledgesSpecifications3DPrintingRoute
    && useBlazorKnowledgesSpecifications3DScanningRoute
    && useBlazorKnowledgesSpecificationsCncMachiningRoute;
builder.AddServiceDefaults();
builder.AddStandardCors();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.AddStandardOpenApi(
    title: "Legacy MALIEV Web BFF",
    description: "Server-rendered compatibility frontend and BFF for the independently deployable legacy services.");
builder.Services.AddSingleton(BuildIdentity.FromConfiguration(builder.Configuration));
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
builder.Services.AddRazorPages(options =>
{
    if (useBlazorRouteHost)
    {
        options.Conventions.AddPageRouteModelConvention(
            "/Index",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/About/Index",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/About/SocialMedia",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Legal/Index",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Legal/NonDisclosureAgreement",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Account/AccessDenied",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Error",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Account/Index",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Account/Login",
            model =>
            {
                foreach (var selector in model.Selectors)
                {
                    selector.EndpointMetadata.Add(new HttpMethodMetadata(["POST"]));
                }
            });
        options.Conventions.AddPageRouteModelConvention(
            "/Account/Signup",
            model =>
            {
                foreach (var selector in model.Selectors)
                {
                    selector.EndpointMetadata.Add(new HttpMethodMetadata(["POST"]));
                }
            });
        options.Conventions.AddPageRouteModelConvention(
            "/Account/ForgotPassword",
            model =>
            {
                foreach (var selector in model.Selectors)
                {
                    selector.EndpointMetadata.Add(new HttpMethodMetadata(["POST"]));
                }
            });
        options.Conventions.AddPageRouteModelConvention(
            "/Account/ResetPassword",
            model =>
            {
                foreach (var selector in model.Selectors)
                {
                    selector.EndpointMetadata.Add(new HttpMethodMetadata(["POST"]));
                }
            });
        options.Conventions.AddPageRouteModelConvention(
            "/Account/Logout",
            model =>
            {
                foreach (var selector in model.Selectors)
                {
                    selector.EndpointMetadata.Add(new HttpMethodMetadata(["POST"]));
                }
            });
        options.Conventions.AddPageRouteModelConvention(
            "/Account/EmailConfirmation",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Account/ChangeEmailConfirmation",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Contact/Index",
            model =>
            {
                foreach (var selector in model.Selectors)
                {
                    selector.EndpointMetadata.Add(new HttpMethodMetadata(["POST"]));
                }
            });
        options.Conventions.AddPageRouteModelConvention(
            "/Quotation/Index",
            model =>
            {
                foreach (var selector in model.Selectors)
                {
                    selector.EndpointMetadata.Add(new HttpMethodMetadata(["POST"]));
                }
            });
        options.Conventions.AddPageRouteModelConvention(
            "/InstantQuotation/3D-Printing",
            model =>
            {
                foreach (var selector in model.Selectors)
                {
                    selector.EndpointMetadata.Add(new HttpMethodMetadata(["POST"]));
                }
            });
        options.Conventions.AddPageRouteModelConvention(
            "/Legal/PrivacyPolicy",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Legal/TermsConditions",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Career/Index",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Career/View",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Services/Index",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Services/Custom-Manufacturing",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Services/CNC-Machining",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Services/3D-Printing",
            model => model.Selectors.Clear());
        options.Conventions.AddPageRouteModelConvention(
            "/Services/3D-Scanning",
            model => model.Selectors.Clear());
    }

    if (useBlazorRouteHost)
    {
        options.Conventions.AddAreaPageRouteModelConvention(
            "Member",
            "/Index",
            model => model.Selectors.Clear());
        options.Conventions.AddAreaPageRouteModelConvention(
            "Member",
            "/Account/Index",
            model => model.Selectors.Clear());
        options.Conventions.AddAreaPageRouteModelConvention(
            "Member",
            "/Orders/Index",
            model => model.Selectors.Clear());
        options.Conventions.AddAreaPageRouteModelConvention(
            "Member",
            "/Orders/History",
            model => model.Selectors.Clear());
        options.Conventions.AddAreaPageRouteModelConvention(
            "Member",
            "/Quotations/Index",
            model => model.Selectors.Clear());
        options.Conventions.AddAreaPageRouteModelConvention(
            "Member",
            "/Orders/View",
            model =>
            {
                foreach (var selector in model.Selectors)
                {
                    selector.EndpointMetadata.Add(new HttpMethodMetadata(["POST"]));
                }
            });
        options.Conventions.AddAreaPageRouteModelConvention(
            "Member",
            "/Quotations/View",
            model => model.Selectors.Clear());
        foreach (var page in new[]
        {
            "/Account/Manage/Profile",
            "/Account/Manage/Address",
            "/Account/Manage/ChangeEmail",
            "/Account/Manage/ChangePassword",
        })
        {
            options.Conventions.AddAreaPageRouteModelConvention(
                "Member",
                page,
                model =>
                {
                    foreach (var selector in model.Selectors)
                    {
                        selector.EndpointMetadata.Add(new HttpMethodMetadata(["POST"]));
                    }
                });
        }
        options.Conventions.AddAreaPageRouteModelConvention(
            "Knowledges",
            "/Index",
            model => model.Selectors.Clear());
        options.Conventions.AddAreaPageRouteModelConvention(
            "Knowledges",
            "/Workflow",
            model => model.Selectors.Clear());
        options.Conventions.AddAreaPageRouteModelConvention(
            "Knowledges",
            "/Guidelines",
            model => model.Selectors.Clear());
        options.Conventions.AddAreaPageRouteModelConvention(
            "Knowledges",
            "/Specifications/Index",
            model => model.Selectors.Clear());
        options.Conventions.AddAreaPageRouteModelConvention(
            "Knowledges",
            "/Specifications/3D-Printing",
            model => model.Selectors.Clear());
        options.Conventions.AddAreaPageRouteModelConvention(
            "Knowledges",
            "/Specifications/3D-Scanning",
            model => model.Selectors.Clear());
        options.Conventions.AddAreaPageRouteModelConvention(
            "Knowledges",
            "/Specifications/CNC-Machining",
            model => model.Selectors.Clear());
    }
})
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IInstantQuotationPricingService, InstantQuotationPricingService>();
builder.Services.AddScoped<IInstantQuotationAnalyticsSink, JsInstantQuotationAnalyticsSink>();
builder.Services.AddScoped<IInstantQuotationAnalyticsTracker, InstantQuotationAnalyticsTracker>();
builder.Services.AddScoped<IInstantQuotationSubmissionService, InstantQuotationSubmissionService>();
builder.Services.AddSingleton<InstantQuotationSessionIdentityCookie>();
builder.Services.AddScoped<
    IInstantQuotationWorkflowSessionIdentityAccessor,
    AuthenticationStateInstantQuotationWorkflowSessionIdentityAccessor>();
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
    redisOptions.Protocol = RedisProtocol.Resp2;
    var redis = ConnectionMultiplexer.Connect(redisOptions);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis);
        options.InstanceName = "legacy:web:";
    });
    dataProtection.PersistKeysToStackExchangeRedis(redis, "legacy:web:data-protection-keys");
    builder.Services.AddHealthChecks().AddRedis(
        redis,
        name: "redis-sessions",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(10));
}

var app = builder.Build();
app.UseMiddleware<BuildIdentityHeaderMiddleware>();
app.UseStandardMiddleware();
app.UseMiddleware<WebContentSecurityPolicyMiddleware>();
app.UseExceptionHandler("/Error");
app.UseWhen(
    static context => !context.Request.Path.StartsWithSegments("/Error", StringComparison.OrdinalIgnoreCase),
    branch => branch.UseStatusCodePagesWithReExecute("/Error", "?code={0}"));
app.UseMiddleware<ErrorResponseContractMiddleware>();
app.UseResponseCompression();
app.UseStaticFiles();
app.UseCookiePolicy();
app.UseRequestLocalization();
app.UseMiddleware<CanonicalUrlRedirectMiddleware>();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<InstantQuotationSessionIdentityMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();
app.UseOutputCache();
if (useBlazorRouteHost)
{
    app.UseWhen(
        InstantQuotationCompatibilityEndpoint.Matches,
        branch => branch.Run(InstantQuotationCompatibilityEndpoint.HandleAsync));
}
app.MapDefaultEndpoints("web");
app.MapBuildIdentity();
app.MapLegacySitemap();
app.MapMemberCompatibilityEndpoints();
if (useBlazorRouteHost)
{
    var razorComponents = app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();
    razorComponents.Add(endpointBuilder =>
    {
        if (endpointBuilder.Metadata.OfType<ComponentTypeMetadata>().Any())
        {
            endpointBuilder.Metadata.Add(new HttpMethodMetadata(["GET", "HEAD"]));
        }
    });
    app.MapPost(
            "/",
            ([FromForm] string culture, [FromQuery] string? returnUrl, HttpContext context) =>
            {
                if (culture is not ("th" or "en"))
                {
                    culture = "th";
                }

                context.Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddYears(1),
                        HttpOnly = true,
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax,
                        Secure = true
                    });

                return Results.LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "~/" : returnUrl);
            })
        .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
}
app.MapRazorPages();
app.MapApiDocumentation(servicePrefix: "web");
await app.RunAsync();

public partial class Program;
