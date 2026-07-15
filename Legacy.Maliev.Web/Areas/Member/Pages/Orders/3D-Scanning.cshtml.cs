using Microsoft.AspNetCore.Authorization;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

[Authorize]
public sealed class ThreeDimensionalScanning() : ServiceOrderCompatibilityPage("3D-Scanning");
