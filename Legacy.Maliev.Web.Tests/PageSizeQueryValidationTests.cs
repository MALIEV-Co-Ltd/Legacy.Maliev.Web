using Microsoft.AspNetCore.Mvc;
using CareerIndex = Legacy.Maliev.Web.Pages.About.Career.Index;
using OrderHistory = Legacy.Maliev.Web.Areas.Member.Pages.Orders.History;
using QuotationIndex = Legacy.Maliev.Web.Areas.Member.Pages.Quotations.Index;

namespace Legacy.Maliev.Web.Tests;

public sealed class PageSizeQueryValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task RetainedRazorListHandlers_RejectOutOfRangeSizeBeforeDownstreamWork(int size)
    {
        var career = new CareerIndex(null!, null!);
        var orders = new OrderHistory(null!, null!) { PageSize = size };
        var quotations = new QuotationIndex(null!, null!) { PageSize = size };

        IActionResult[] results =
        [
            await career.OnGetAsync(null, null, null, size, CancellationToken.None),
            await orders.OnGetAsync(CancellationToken.None),
            await quotations.OnGetAsync(CancellationToken.None),
            await career.OnGetSearchAsync(null, null, null, size, CancellationToken.None),
            await career.OnGetChangeItemCountAsync(size, CancellationToken.None),
        ];

        Assert.All(results, result => Assert.IsType<BadRequestResult>(result));
    }

    [Fact]
    public async Task RetainedRazorListHandlers_RejectMalformedBoundSizeBeforeDownstreamWork()
    {
        var career = new CareerIndex(null!, null!);
        var orders = new OrderHistory(null!, null!);
        var quotations = new QuotationIndex(null!, null!);
        career.ModelState.AddModelError("size", "The value 'invalid-size' is not valid.");
        orders.ModelState.AddModelError("size", "The value 'invalid-size' is not valid.");
        quotations.ModelState.AddModelError("size", "The value 'invalid-size' is not valid.");

        IActionResult[] results =
        [
            await career.OnGetAsync(null, null, null, 25, CancellationToken.None),
            await orders.OnGetAsync(CancellationToken.None),
            await quotations.OnGetAsync(CancellationToken.None),
            await career.OnGetSearchAsync(null, null, null, 25, CancellationToken.None),
            await career.OnGetChangeItemCountAsync(25, CancellationToken.None),
        ];

        Assert.All(results, result => Assert.IsType<BadRequestResult>(result));
    }
}
