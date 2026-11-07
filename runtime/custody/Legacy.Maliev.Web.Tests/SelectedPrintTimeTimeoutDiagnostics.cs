using System.Globalization;
using System.Text.Json;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Test-only bounded metadata; never records DOM text, tickets, payloads or secondary exceptions.</summary>
internal static class SelectedPrintTimeTimeoutDiagnostics
{
    internal const string DataKey = "NineUploadSelectedPrintTime";

    internal static bool IsMaterialChangeComplete(InstantQuotationSessionState? current,
        DateTimeOffset before, Guid partId) => current is not null
            && current.UpdatedAt != before
            && current.QuoteAuthorization is not null
            && current.Parts.Any(part => part.PartId == partId
                && part.Configuration.MaterialKey == "ABS" && part.Configuration.Quantity == 1);

    internal static async Task AttachAsync(Exception original, int iteration, string? expectedPartId, Func<Task<string>> observe,
        Action<string>? emit = null)
    {
        // This secondary diagnostic must never replace or wrap the original Playwright timeout.
        var description = "observation=unavailable";
        try
        {
            var observation = observe();
            _ = observation.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var json = await observation.WaitAsync(TimeSpan.FromSeconds(1));
            if (json.Length > 1024) throw new FormatException();
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "workflow") is null) throw new FormatException();
            var state = new BoundedState(
                iteration is >= 1 and <= 9 ? iteration : null,
                PartId(expectedPartId), PartId(Text(root, "actualPartId")), PartId(Text(root, "configurationPartId")),
                Workflow(Text(root, "workflow")), Boolean(root, "isRepricing"),
                Text(root, "material") switch { "PLA" => Material.PLA, "ABS" => Material.ABS, _ => Material.Unknown },
                Quantity(Text(root, "quantity")), Boolean(root, "durationPresent"), Boolean(root, "unavailablePresent"));
            description = state.Describe();
        }
        catch { /* Secondary observation failure is represented only by the fixed unavailable marker. */ }
        try
        {
            original.Data[DataKey] = description;
            emit?.Invoke(description);
        }
        catch { /* Even an unwritable Data dictionary or failing sink cannot replace the primary failure. */ }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: <= 128 } text ? text : null;

    private static bool? Boolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) ? value.ValueKind switch
        { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;

    private static Guid? PartId(string? value) => value is { Length: 32 or 36 }
        && (Guid.TryParseExact(value, "N", out var id) || Guid.TryParseExact(value, "D", out id)) ? id : null;

    private static int? Quantity(string? value) => int.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 10000 ? number : null;

    private static Category Workflow(string? value) => value switch
    {
        "empty" => Category.Empty,
        "uploading" => Category.Uploading,
        "uploaded" => Category.Uploaded,
        "error" => Category.Error,
        "multipart" => Category.MultiPart,
        "configured" => Category.Configured,
        "review" => Category.Review,
        "customerdetails" => Category.CustomerDetails,
        "submitted" => Category.Submitted,
        _ => Category.Unknown,
    };

    private enum Category { Unknown, Empty, Uploading, Uploaded, Error, MultiPart, Configured, Review, CustomerDetails, Submitted }
    private enum Material { Unknown, PLA, ABS }

    private sealed record BoundedState(int? Iteration, Guid? ExpectedPart, Guid? ActualPart, Guid? ConfigurationPart,
        Category Workflow, bool? IsRepricing, Material Material, int? VisibleQuantity, bool? DurationPresent, bool? UnavailablePresent)
    {
        internal string Describe() => string.Join(';',
            $"iteration={Number(Iteration)}", $"expectedPart={Identity(ExpectedPart)}", $"actualPart={Identity(ActualPart)}",
            $"configurationPart={Identity(ConfigurationPart)}", $"workflow={Workflow}", $"isRepricing={Flag(IsRepricing)}",
            $"material={Material}", $"visibleQuantity={Number(VisibleQuantity)}", $"durationPresent={Flag(DurationPresent)}",
            $"unavailablePresent={Flag(UnavailablePresent)}");

        private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
        private static string Identity(Guid? value) => value?.ToString("N") ?? "unknown";
        private static string Flag(bool? value) => value?.ToString() ?? "unknown";
    }
}
