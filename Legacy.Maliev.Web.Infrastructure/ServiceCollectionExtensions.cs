using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLegacyAccountAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.AccessDeniedPath = "/Account/AccessDenied";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.Name = "__Host-Maliev.Legacy.Session";
                options.Cookie.Path = "/";
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
                options.EventsType = typeof(AccountCookieEvents);
                options.LoginPath = "/Account/Login";
                options.SlidingExpiration = false;
            });
        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddLegacyServiceClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ServiceEndpoints>()
            .Bind(configuration.GetSection("Services"))
            .ValidateOnStart();
        services.AddOptions<ServiceAuthenticationOptions>()
            .Bind(configuration.GetSection("ServiceAuthentication"));
        services.AddOptions<RecaptchaEnterpriseOptions>()
            .Bind(configuration.GetSection("Recaptcha"));

        AddClient(services, "auth", static endpoints => endpoints.Auth);
        AddClient(services, "careers", static endpoints => endpoints.Career);
        AddClient(services, "catalog", static endpoints => endpoints.Catalog);
        AddClient(services, "contacts", static endpoints => endpoints.Contact);
        AddClient(services, "countries", static endpoints => endpoints.Country);
        AddClient(services, "customers", static endpoints => endpoints.Customer);
        AddClient(services, "documents", static endpoints => endpoints.Document);
        AddClient(services, "files", static endpoints => endpoints.File);
        AddClient(services, "orders", static endpoints => endpoints.Order);
        AddClient(services, "notifications", static endpoints => endpoints.Notification);
        AddClient(services, "quotations", static endpoints => endpoints.Quotation);
        services.AddScoped<ICareerClient, CareerClient>();
        services.AddScoped<ICountryClient, CountryClient>();
        services.AddScoped<IContactClient, ContactClient>();
        services.AddScoped<IQuotationClient, QuotationClient>();
        services.AddScoped<ICustomerQuotationClient, CustomerQuotationClient>();
        services.AddScoped<IQuotationFileClient, QuotationFileClient>();
        services.AddScoped<INotificationClient, NotificationClient>();
        services.AddScoped<ICustomerAuthenticationClient, CustomerAuthenticationClient>();
        services.AddScoped<ICustomerProfileClient, CustomerProfileClient>();
        services.AddScoped<ICustomerAccountClient, CustomerAccountClient>();
        services.AddScoped<ICustomerOrderClient, CustomerOrderClient>();
        services.AddSingleton<IAccountSessionStore, DistributedAccountSessionStore>();
        services.AddSingleton<IInstantQuotationSessionStore, DistributedInstantQuotationSessionStore>();
        services.AddSingleton<IInstantQuotationFileCapabilityStore, InstantQuotationFileCapabilityStore>();
        services.AddSingleton<IInstantQuotationSubmissionStore, InstantQuotationSubmissionStore>();
        services.AddSingleton<IInstantQuotationUploadClient, UnavailableInstantQuotationUploadClient>();
        services.AddScoped<IAccountSessionManager, AccountSessionManager>();
        services.AddScoped<AccountCookieEvents>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IServiceAccessTokenProvider, ServiceAccessTokenProvider>();
        services.AddSingleton<IRecaptchaAssessmentClient, GoogleRecaptchaAssessmentClient>();
        services.AddScoped<IAntiBotVerifier, RecaptchaEnterpriseVerifier>();
        return services;
    }

    private static void AddClient(
        IServiceCollection services,
        string name,
        Func<ServiceEndpoints, Uri> resolveBaseAddress)
    {
        services.AddHttpClient(name, (provider, client) =>
        {
            client.BaseAddress = resolveBaseAddress(provider.GetRequiredService<IOptions<ServiceEndpoints>>().Value);
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddStandardResilienceHandler(options =>
            options.Retry.DisableForUnsafeHttpMethods());
    }
}
