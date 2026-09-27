using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Maliev.Aspire.ServiceDefaults.Middleware;
using Maliev.Aspire.ServiceDefaults.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

// Console.Out is process-wide; keep this wire proof isolated from other test collections.
[CollectionDefinition("Shared diagnostics output", DisableParallelization = true)]
public sealed class SharedDiagnosticsOutputCollection;

[Collection("Shared diagnostics output")]
public sealed class SharedDiagnosticsContractTests
{
    [Fact]
    public async Task WebSharedMiddleware_LogsMappedNotFoundAtDebugWithRouteTemplate()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/customers/private@example.com";
        context.Request.QueryString = new QueryString("?token=private-query");
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/customers/{id}"),
            0,
            new EndpointMetadataCollection(),
            "customer detail"));
        var logger = new CapturingMiddlewareLogger();
        var environment = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Legacy.Maliev.Web",
            EnvironmentName = Environments.Production,
        }).Environment;
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new KeyNotFoundException("private-exception-detail"),
            logger,
            environment);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(LogLevel.Debug, logger.Level);
        Assert.Null(logger.Exception);
        Assert.Equal("/customers/{id}", logger.Fields["Path"]);
        Assert.Equal(StatusCodes.Status404NotFound, logger.Fields["StatusCode"]);
        Assert.DoesNotContain("private-", logger.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WebSharedDefaults_RedactDependencyPathAndEmitCloudCriticalSeverity()
    {
        using var activity = new Activity("web-dependency").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.SetTag("url.full", "https://orders.maliev.internal/customers/private%40example.com?token=private-query");
        activity.SetTag("url.path", "/orders/42");
        new UrlQueryRedactionProcessor().OnEnd(activity);

        Assert.Equal("https://orders.maliev.internal/customers/{identifier}?<redacted>", activity.GetTagItem("url.full"));
        Assert.Equal("/orders/{id}", activity.GetTagItem("url.path"));

        var original = Console.Out;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(output);
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = null;
            builder.AddServiceDefaults();
            using var host = builder.Build();
            var logger = ((ILoggerFactory)host.Services.GetService(typeof(ILoggerFactory))!).CreateLogger("Web.SharedDiagnostics");
            logger.LogCritical(new InvalidOperationException("private-exception-detail"), "Safe dependency failure");
        }
        finally
        {
            Console.SetOut(original);
        }

        var entry = Assert.Single(output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            }), value => value.GetProperty("Category").GetString() == "Web.SharedDiagnostics");
        Assert.Equal("Critical", entry.GetProperty("LogLevel").GetString());
        Assert.Equal("CRITICAL", entry.GetProperty("severity").GetString());
        Assert.Equal("InvalidOperationException", entry.GetProperty("Exception").GetString());
        Assert.Equal("Safe dependency failure", entry.GetProperty("Message").GetString());
        Assert.Contains(entry.GetProperty("Scopes").EnumerateArray(), scope =>
            scope.TryGetProperty("TraceId", out var trace) && trace.GetString() == activity.TraceId.ToString());
        Assert.DoesNotContain("private-exception-detail", output.ToString(), StringComparison.Ordinal);
    }

    private sealed class CapturingMiddlewareLogger : ILogger<ExceptionHandlingMiddleware>
    {
        public LogLevel Level { get; private set; }
        public Exception? Exception { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public Dictionary<string, object?> Fields { get; private set; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Level = logLevel;
            Exception = exception;
            Message = formatter(state, exception);
            Fields = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.Where(value => value.Key != "{OriginalFormat}").ToDictionary(value => value.Key, value => value.Value)
                : [];
        }
    }
}
