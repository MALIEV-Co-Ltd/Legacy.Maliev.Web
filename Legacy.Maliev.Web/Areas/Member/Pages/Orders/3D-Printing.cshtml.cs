using Microsoft.AspNetCore.Authorization;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

[Authorize]
public sealed class ThreeDimensionalPrinting() : ServiceOrderCompatibilityPage("3D-Printing");
