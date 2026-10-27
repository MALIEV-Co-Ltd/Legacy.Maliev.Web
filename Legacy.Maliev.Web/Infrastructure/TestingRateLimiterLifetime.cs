using System.Reflection;
using System.Threading.RateLimiting;
using RateLimiterOptions = Microsoft.AspNetCore.RateLimiting.RateLimiterOptions;
using Microsoft.AspNetCore.Http.Features;

namespace Legacy.Maliev.Web.Infrastructure;

/// <summary>Releases the framework-created endpoint limiter of an owned Testing pipeline.</summary>
internal sealed class TestingRateLimiterLifetime : IDisposable
{
    private readonly object gate = new();
    private PartitionedRateLimiter<HttpContext>? endpoint;
    private int disposalState;

    internal void Use(IApplicationBuilder application)
    {
        var middleware = typeof(RateLimiterOptions).Assembly.GetType("Microsoft.AspNetCore.RateLimiting.RateLimitingMiddleware", throwOnError: true)!;
        var field = ValidateMiddleware(middleware);
        new CreationSite(application, this, middleware, field).Register();
    }

    internal static FieldInfo ValidateMiddleware(Type middleware)
    {
        var invoke = middleware.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
        var field = middleware.GetField("_endpointLimiter", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (middleware.Assembly != typeof(RateLimiterOptions).Assembly
            || middleware.FullName != "Microsoft.AspNetCore.RateLimiting.RateLimitingMiddleware"
            || !middleware.IsSealed || invoke?.ReturnType != typeof(Task)
            || invoke.GetParameters() is not [{ ParameterType: var parameter }] || parameter != typeof(HttpContext)
            || field is not { IsPrivate: true, IsInitOnly: true } || field.FieldType != typeof(PartitionedRateLimiter<HttpContext>))
            throw new InvalidOperationException("Unsupported Testing rate-limiter framework shape.");
        return field;
    }

    internal void Attach(PartitionedRateLimiter<HttpContext> created)
    {
        lock (gate)
        {
            if (disposalState == 0 && endpoint is null)
            {
                endpoint = created;
                return;
            }
            var failure = new InvalidOperationException("Testing endpoint limiter ownership was already assigned or released.");
            if (!ReferenceEquals(endpoint, created))
            {
                try { created.Dispose(); }
                catch (Exception cleanup) { failure.Data["EndpointCleanupFailureType"] = cleanup.GetType().FullName; }
            }
            throw failure;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposalState != 0) return;
            disposalState = 1;
            try { endpoint?.Dispose(); }
            finally { endpoint = null; disposalState = 2; }
        }
    }

    private sealed class CreationSite(IApplicationBuilder application, TestingRateLimiterLifetime owner, Type middleware, FieldInfo field) : IApplicationBuilder
    {
        private int registrations;
        public IServiceProvider ApplicationServices { get => application.ApplicationServices; set => application.ApplicationServices = value; }
        public IFeatureCollection ServerFeatures => application.ServerFeatures;
        public IDictionary<string, object?> Properties => application.Properties;
        internal void Register()
        {
            this.UseRateLimiter();
            if (registrations != 1) throw new InvalidOperationException("Unsupported Testing rate-limiter registration count.");
        }
        public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> factory)
        {
            if (++registrations != 1) throw new InvalidOperationException("Unsupported Testing rate-limiter registration count.");
            application.Use(next =>
            {
                var request = factory(next);
                if (request.Target?.GetType() != middleware
                    || field.GetValue(request.Target) is not PartitionedRateLimiter<HttpContext> created)
                    throw new InvalidOperationException("Unsupported Testing rate-limiter delegate shape.");
                if (request.Method.Name != "Invoke" || request.Method.DeclaringType != middleware
                    || request.GetInvocationList().Length != 1)
                {
                    var failure = new InvalidOperationException("Unsupported Testing rate-limiter invocation shape.");
                    try { created.Dispose(); }
                    catch (Exception cleanup) { failure.Data["EndpointCleanupFailureType"] = cleanup.GetType().FullName; }
                    throw failure;
                }
                owner.Attach(created);
                return request;
            });
            return this;
        }
        public IApplicationBuilder New() => application.New();
        public RequestDelegate Build() => application.Build();
    }
}
