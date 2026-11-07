using System.Text.Json;
using Legacy.Maliev.Web.Pages.Shared;

namespace Legacy.Maliev.Web.Tests;

public sealed class ServiceFinderHandoffTests
{
    [Fact]
    public void TryCreate_StoresOnlyAllowlistedStableIds()
    {
        var created = ServiceFinderAttribution.TryCreate(
            "files-3d",
            "service-3d",
            "material-plastic",
            "quantity-1-10",
            "use-prototype",
            "printing",
            "printing",
            "performance-strength",
            "environment-indoor",
            out var attribution);

        Assert.True(created);
        Assert.NotNull(attribution);
        using var document = JsonDocument.Parse(attribution!.ToMetadataJson());
        Assert.Equal("service_finder", document.RootElement.GetProperty("source").GetString());
        Assert.Equal("files-3d", document.RootElement.GetProperty("answers").GetProperty("files").GetString());
        Assert.Equal("printing", document.RootElement.GetProperty("finder_path")[0].GetString());
        Assert.DoesNotContain("email", document.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not-allowlisted", "service-3d", "material-plastic", "quantity-1-10", "use-prototype", "printing", "printing")]
    [InlineData("files-3d", "service-3d", "material-plastic", "quantity-1-10", "use-prototype", "javascript", "javascript")]
    [InlineData("files-3d", "service-3d", "material-plastic", "quantity-1-10", "use-prototype", "printing", "cnc")]
    public void TryCreate_RejectsTamperedStableIds(
        string files,
        string service,
        string material,
        string quantity,
        string endUse,
        string recommendations,
        string path)
    {
        Assert.False(ServiceFinderAttribution.TryCreate(
            files,
            service,
            material,
            quantity,
            endUse,
            recommendations,
            path,
            null,
            null,
            out _));
    }

    [Fact]
    public void MetadataEnvelope_RejectsUnknownServiceIds()
    {
        var raw = "{\"source\":\"service_finder\",\"version\":1,\"answers\":{\"files\":\"files-3d\",\"service\":\"service-3d\",\"material\":\"material-plastic\",\"quantity\":\"quantity-1-10\",\"end-use\":\"use-prototype\"},\"recommended_service_ids\":[\"javascript\"],\"finder_path\":[\"javascript\"]}";
        Assert.False(ServiceFinderMetadataEnvelope.TryRead(raw, out _));
    }

    [Fact]
    public void ServiceFinder_RealPartAnswerPersistsScanningFirstPath()
    {
        Assert.True(ServiceFinderMetadataEnvelope.IsAllowedAnswer("files", "files-real-part"));
        Assert.True(ServiceFinderAttribution.TryCreate(
            "files-real-part",
            "service-3d",
            "material-standard-plastic",
            "quantity-1-10",
            "use-replacement",
            "scanning,printing",
            "scanning,printing",
            out var attribution));

        Assert.NotNull(attribution);
        Assert.Equal("files-real-part", attribution.Answers["files"]);
        Assert.Equal("scanning>printing", attribution.FinderPathValue);
        Assert.True(ServiceFinderMetadataEnvelope.TryRead(attribution.ToMetadataJson(), out var metadata));
        Assert.Contains("Physical part available", metadata.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceFinder_OptionalRequirementsPersistWithoutChangingCorePath()
    {
        Assert.True(ServiceFinderMetadataEnvelope.IsAllowedOptionalAnswer("performance", "performance-strength"));
        Assert.True(ServiceFinderMetadataEnvelope.IsAllowedOptionalAnswer("environment", "environment-outdoor"));
        Assert.True(ServiceFinderAttribution.TryCreate(
            "files-real-part",
            "service-3d",
            "material-standard-plastic",
            "quantity-1-10",
            "use-prototype",
            "scanning,printing",
            "scanning,printing",
            "performance-strength",
            "environment-outdoor",
            out var attribution));

        Assert.NotNull(attribution);
        Assert.Equal("performance-strength", attribution.Answers["performance"]);
        Assert.Equal("environment-outdoor", attribution.Answers["environment"]);
        Assert.True(ServiceFinderMetadataEnvelope.TryRead(attribution.ToMetadataJson(), out var metadata));
        Assert.Equal("performance-strength", metadata.Answers["performance"]);
        Assert.Equal("environment-outdoor", metadata.Answers["environment"]);
        Assert.Contains("Performance: Strength and durability", metadata.Summary, StringComparison.Ordinal);
        Assert.Contains("Environment: Outdoor or UV exposure", metadata.Summary, StringComparison.Ordinal);

        var legacyEnvelope = ServiceFinderMetadataEnvelope.CreateJson(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "files", "files-real-part" },
                { "service", "service-3d" },
                { "material", "material-standard-plastic" },
                { "quantity", "quantity-1-10" },
                { "end-use", "use-prototype" },
            },
            ["scanning", "printing"],
            ["scanning", "printing"]);
        Assert.True(ServiceFinderMetadataEnvelope.TryRead(legacyEnvelope, out var legacyMetadata));
        Assert.False(legacyMetadata.Answers.ContainsKey("performance"));
    }

    [Fact]
    public void ServiceFinderMetadata_RejectsSpoofedPathAndPreservesOperatorComment()
    {
        Assert.False(ServiceFinderAttribution.TryCreate(
            "files-none",
            "service-machining",
            "material-metal",
            "quantity-11-100",
            "use-replacement",
            "printing,cnc",
            "printing,cnc",
            out _));

        Assert.True(ServiceFinderAttribution.TryCreate(
            "files-none",
            "service-machining",
            "material-metal",
            "quantity-11-100",
            "use-replacement",
            "scanning,cnc",
            "scanning,cnc",
            out var attribution));

        Assert.NotNull(attribution);
        var persisted = attribution.ToMetadataJson();
        var updated = ServiceFinderMetadataEnvelope.MergeOperatorComment(persisted, "Qualified by sales");

        Assert.True(ServiceFinderMetadataEnvelope.TryRead(updated, out var metadata));
        Assert.Equal("Qualified by sales", metadata.OperatorComment);
        Assert.Contains("3D scanning -> CNC machining", metadata.Summary, StringComparison.Ordinal);
    }
}
