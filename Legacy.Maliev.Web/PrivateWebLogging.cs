using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Maliev.Aspire.ServiceDefaults.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Legacy.Maliev.Web;

/// <summary>Selects Web-owned private console logs without changing shared telemetry.</summary>
internal static class PrivateWebLogging
{
    internal static void AddPrivateWebConsoleLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Services.AddHttpContextAccessor();
        builder.Logging.AddConsoleFormatter<WebPrivateConsoleFormatter, ConsoleFormatterOptions>();
        builder.Logging.AddConsole(options => options.FormatterName = WebPrivateConsoleFormatter.FormatterName);
        builder.Services.PostConfigure<ConsoleLoggerOptions>(
            options => options.FormatterName = WebPrivateConsoleFormatter.FormatterName);
        builder.Services.PostConfigure<LoggerFilterOptions>(ClampFilters);
    }

    internal static void ClampFilters(LoggerFilterOptions options)
    {
        if (options.MinLevel < LogLevel.Warning)
        {
            options.MinLevel = LogLevel.Warning;
        }
        for (var index = 0; index < options.Rules.Count; index++)
        {
            var rule = options.Rules[index];
            var original = rule.Filter;
            var minimum = rule.LogLevel;
            if (!minimum.HasValue || minimum.Value < LogLevel.Warning)
            {
                minimum = LogLevel.Warning;
            }
            options.Rules[index] = new LoggerFilterRule(rule.ProviderName, rule.CategoryName, minimum,
                (provider, category, level) => level >= LogLevel.Warning
                    && (original?.Invoke(provider, category, level) ?? true));
        }
    }

    internal static async Task<int> RunHostAsync(
        Func<Task> start,
        Func<bool>? preserveTestingException = null,
        TextWriter? bootstrapOutput = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        try
        {
            await start();
            return 0;
        }
        catch (HostAbortedException)
        {
            // HostFactoryResolver uses this signal for tooling, not a startup incident.
            throw;
        }
        catch (Exception exception)
        {
            var fields = new KeyValuePair<string, object?>[]
            {
                new("EventName", "HostStartupFailure"),
                new("Operation", "startup"),
            };
            var entry = new LogEntry<KeyValuePair<string, object?>[]>(
                LogLevel.Critical, "Legacy.Maliev.Web.Program", default, fields, exception,
                static (_, _) => string.Empty);
            new WebPrivateConsoleFormatter().Write(in entry, null, bootstrapOutput ?? Console.Out);
            if (preserveTestingException?.Invoke() == true)
            {
                // Existing Testing factories assert the original validation exception.
                // No process-wide ExitCode mutation or conversion to startup success.
                throw;
            }
            return 1;
        }
    }
}

/// <summary>Writes bounded native source-compatible failure fields without private rendering.</summary>
internal sealed class WebPrivateConsoleFormatter : ConsoleFormatter
{
    internal const string FormatterName = "web-private-cloud-json";
    private const int MaximumFields = 64;
    private const int MaximumRecordBytes = 8192;
    private const string SinkFailure = "{\"severity\":\"CRITICAL\",\"State\":{\"EventName\":\"PrivateLogSinkFailure\",\"Operation\":\"logging\"}}";
    private readonly IHttpContextAccessor? contextAccessor;

    /// <summary>Initializes Web's bounded private console formatter.</summary>
    public WebPrivateConsoleFormatter(IHttpContextAccessor? contextAccessor = null) : base(FormatterName)
    {
        this.contextAccessor = contextAccessor;
    }

    /// <inheritdoc />
    public override void Write<TState>(
        in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        if (logEntry.LogLevel is not (LogLevel.Warning or LogLevel.Error or LogLevel.Critical))
        {
            return;
        }

        try
        {
            ArgumentNullException.ThrowIfNull(textWriter);
            using var buffer = new MemoryStream(2048);
            using (var json = new Utf8JsonWriter(buffer))
            {
                json.WriteStartObject();
                json.WriteString("severity", logEntry.LogLevel switch
                {
                    LogLevel.Warning => "WARNING",
                    LogLevel.Error => "ERROR",
                    _ => "CRITICAL",
                });
                json.WriteString("Timestamp", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                json.WriteString("LogLevel", logEntry.LogLevel.ToString());
                json.WriteNumber("EventId", logEntry.EventId.Id);
                json.WriteString("Category", IsIdentifier(logEntry.Category, 192) ? logEntry.Category : "Application");
                var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                json.WriteString("DeploymentVersion", IsMetadata(version, 256, allowVersion: true) ? version : null);
                WriteException(json, logEntry.Exception);
                WriteOwnedSyntheticIdentity(json, logEntry.Exception);
                json.WritePropertyName("State");
                WriteState(json, logEntry.State);
                json.WriteStartArray("Scopes");
                // Never traverse caller-owned scope providers. Real request traces
                // come from Activity; explicit trace state remains allowlisted below.
                if (Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity
                    && activity.TraceId != default && activity.SpanId != default)
                {
                    json.WriteStartObject();
                    json.WriteString("TraceId", activity.TraceId.ToHexString());
                    json.WriteString("SpanId", activity.SpanId.ToHexString());
                    if (activity.ParentSpanId != default)
                    {
                        json.WriteString("ParentId", activity.ParentSpanId.ToHexString());
                    }
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            if (buffer.Length > MaximumRecordBytes)
            {
                WriteSinkFailure();
                return;
            }
            textWriter.WriteLine(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length)));
        }
        catch (Exception)
        {
            // Neither the original failure nor a sink/serialization error may
            // cause private messages, state or stack text to be rendered.
            WriteSinkFailure();
        }
    }

    private static void WriteSinkFailure()
    {
        try
        {
            Console.Error.WriteLine(SinkFailure);
        }
        catch (Exception)
        {
            // A second broken sink cannot make request handling throw or reveal text.
        }
    }

    private static void WriteException(Utf8JsonWriter json, Exception? failure)
    {
        if (failure is null)
        {
            return;
        }
        var type = failure.GetType().FullName;
        var inner = failure.InnerException?.GetType().FullName;
        var target = failure.TargetSite;
        var declaring = target?.DeclaringType?.FullName;
        var method = target?.Name;
        json.WriteString("Exception", IsMetadata(type, 256) ? type : null);
        json.WriteString("ExceptionType", IsMetadata(type, 256) ? type : null);
        json.WriteString("InnerExceptionType", IsMetadata(inner, 256) ? inner : null);
        json.WriteString("SourceLocation", IsRuntimeLocationPart(declaring, 256) && IsRuntimeLocationPart(method, 96)
            ? declaring + "." + method : null);
    }

    private void WriteOwnedSyntheticIdentity(Utf8JsonWriter json, Exception? failure)
    {
        // The shared observer's exact sealed exception and canonical SERVER
        // response headers preserve its framework-error correlation. Request
        // headers, arbitrary scopes and exception text are never inspected.
        if (failure is not ProductionObservabilityDiagnosticException
            || contextAccessor is not HttpContextAccessor accessor
            || accessor.GetType() != typeof(HttpContextAccessor)
            || accessor.HttpContext is not { } context)
        {
            return;
        }
        var nonce = context.Response.Headers["X-Maliev-Diagnostic-Id"];
        var instance = context.Response.Headers["X-Maliev-Health-Instance"];
        if (nonce.Count == 1 && instance.Count == 1
            && nonce[0] is { Length: 32 } value && instance[0] is { Length: 32 } health
            && Guid.TryParseExact(value, "N", out var parsed)
            && Guid.TryParseExact(health, "N", out _))
        {
            json.WriteBoolean("Synthetic", true);
            json.WriteString("DiagnosticId", parsed.ToString("N"));
        }
    }

    private static void WriteState<TState>(Utf8JsonWriter json, TState state)
    {
        json.WriteStartObject();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (state is KeyValuePair<string, object?>[] array)
        {
            for (var index = 0; index < Math.Min(array.Length, MaximumFields); index++)
            {
                WriteField(json, seen, array[index]);
            }
        }
        else if (state is Dictionary<string, object?> dictionary
            && dictionary.GetType() == typeof(Dictionary<string, object?>))
        {
            var count = 0;
            foreach (var field in dictionary)
            {
                if (count++ == MaximumFields)
                {
                    break;
                }
                WriteField(json, seen, field);
            }
        }
        else if (state is IReadOnlyList<KeyValuePair<string, object?>> formatted
            && formatted.GetType().Assembly == typeof(LoggerExtensions).Assembly
            && formatted.GetType().FullName == "Microsoft.Extensions.Logging.FormattedLogValues")
        {
            // The framework indexer returns the original key/value pair; it
            // does not invoke FormattedLogValues.ToString or the log formatter.
            for (var index = 0; index < Math.Min(formatted.Count, MaximumFields); index++)
            {
                WriteField(json, seen, formatted[index]);
            }
        }
        json.WriteEndObject();
    }

    private static void WriteField(
        Utf8JsonWriter json, HashSet<string> seen, KeyValuePair<string, object?> field)
    {
        var key = field.Key;
        if (key is null || key.Length > 32 || !seen.Add(key))
        {
            return;
        }
        var value = field.Value as string;
        if (key is "EventName" or "Service" or "Method" or "Operation" or "Dependency" or "ExceptionType"
            && IsIdentifier(value, 128))
        {
            json.WriteString(key, value);
        }
        else if (key == "Code" && value is "upload_invalid" or "quote_invalid" or "quote_expired" or "upload_expired" or "quote_mismatch")
        {
            json.WriteString(key, value);
        }
        else if (key == "StatusCode" && field.Value is int status && status is >= 100 and <= 599)
        {
            json.WriteNumber(key, status);
        }
        else if (key == "Synthetic" && field.Value is bool synthetic)
        {
            json.WriteBoolean(key, synthetic);
        }
        else if (key == "DiagnosticId" && value is { Length: 32 }
            && Guid.TryParseExact(value, "N", out var nonce))
        {
            json.WriteString(key, nonce.ToString("N"));
        }
        else if (key == "IncidentId" && value is { Length: >= 1 and <= 96 }
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '_' or '-'))
        {
            json.WriteString(key, value);
        }
        else if (key is "TraceId" or "SpanId" && IsTrace(value))
        {
            json.WriteString(key, value);
        }
        else if (key == "OccurredAtUtc" && value is { Length: >= 1 and <= 64 }
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var occurred))
        {
            json.WriteString(key, occurred.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }
    }

    private static bool IsIdentifier(string? value, int maximum) =>
        value is { Length: >= 1 } && value.Length <= maximum
        && char.IsAsciiLetter(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-');

    private static bool IsMetadata(string? value, int maximum, bool allowVersion = false) =>
        value is { Length: >= 1 } && value.Length <= maximum
        && (char.IsAsciiLetterOrDigit(value[0]) || value[0] == '_')
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '_' or '.' or '+' || character == (char)96 || (allowVersion && character == '-'));

    // Only runtime-owned MethodBase metadata uses this grammar. Compiler async,
    // closure and local-function names contain angle brackets, bars and dollars.
    // Caller state/category/version grammars remain unchanged.
    private static bool IsRuntimeLocationPart(string? value, int maximum) =>
        value is { Length: >= 1 } && value.Length <= maximum
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '_' or '.' or '+' or '<' or '>' or '|'
            || character == (char)36 || character == (char)96);

    private static bool IsTrace(string? value) => value is { Length: 16 or 32 }
        && value.All(character => char.IsAsciiHexDigit(character));
}
