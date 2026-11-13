using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Web.Components.Pages.Home;

/// <summary>Shares production/Development-only hero selection across static SSR and Razor fallback.</summary>
internal static class HomeHeroSelection
{
    internal static string GetVariant(HttpContext? context, IWebHostEnvironment environment)
    {
        var requested = context?.Request.Query["hero"].ToString();
        return environment.IsDevelopment()
            && context?.Request.Query["preview"].ToString() == "1"
            && (requested is "assembly" or "partnership" or "journey")
                ? requested
                : "carousel";
    }

    internal static string GetPreload(string variant) => variant switch
    {
        "partnership" => "/src/images/landing/camera-bracket-deployment.webp",
        "journey" => "/src/images/landing/camera-bracket-sketch-v6.webp",
        _ => "/src/images/landing/profile-hybrid-gripper-cutout.webp",
    };

    internal static string LocalizedLink(string path, string culture) =>
        path + "?culture=" + Uri.EscapeDataString(culture);

    internal static string PreviewLink(string culture, string hero) =>
        LocalizedLink("/", culture) + "&hero=" + Uri.EscapeDataString(hero) + "&preview=1";
}
