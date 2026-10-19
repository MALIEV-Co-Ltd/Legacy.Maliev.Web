using System.Net.Http.Headers;

namespace Legacy.Maliev.Web.Infrastructure;

internal static class UploadContentTypeResolver
{
    internal static MediaTypeHeaderValue Resolve(string? contentType)
    {
        if (contentType == "simplify3d_stl")
        {
            contentType = "simplify3d_stl/stl";
        }

        return !string.IsNullOrWhiteSpace(contentType)
            && MediaTypeHeaderValue.TryParse(contentType, out var parsed)
                ? parsed
                : new MediaTypeHeaderValue("application/octet-stream");
    }
}
