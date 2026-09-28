using System.Reflection;
using Legacy.Maliev.Web.Areas.Member.Pages.Orders;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberOrderUploadLimitTests
{
    [Fact]
    public void MemberOrderForm_EnforcesEdgeCompatibleAggregateLimit()
    {
        var model = typeof(MemberOrderCreatePageModel);
        var maximumUploadBytes = model.GetField("MaximumUploadBytes", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(maximumUploadBytes);
        Assert.Equal(100L * 1024 * 1024, maximumUploadBytes.GetRawConstantValue());
        Assert.Equal(115_343_360,
            model.GetCustomAttribute<RequestFormLimitsAttribute>()?.MultipartBodyLengthLimit);
        Assert.Equal(115_343_360,
            ((IRequestSizeLimitMetadata?)model.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize);
    }
}
