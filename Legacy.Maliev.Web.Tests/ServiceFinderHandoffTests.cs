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
}
