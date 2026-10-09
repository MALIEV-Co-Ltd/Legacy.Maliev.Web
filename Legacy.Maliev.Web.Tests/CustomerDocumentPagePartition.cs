using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Legacy.Maliev.Web.Components.Pages.Member;

namespace Legacy.Maliev.Web.Tests;

internal static class CustomerDocumentPagePartition
{
    public static string[] LegacyPagesAfterVerifyingDocuments(this IEnumerable<string> routedPagePaths)
    {
        var routedPages = routedPagePaths.ToArray();
        var documentPage = Assert.Single(routedPages.Where(path =>
            string.Equals(Path.GetFileName(path), "MemberDocumentsPage.razor", StringComparison.Ordinal)));
        Assert.EndsWith("/Components/Pages/Member/MemberDocumentsPage.razor", documentPage.Replace('\\', '/'), StringComparison.Ordinal);
        var type = typeof(MemberDocumentsPage);
        Assert.Equal(new[] { "/Member/Documents" }, type.GetCustomAttributes<RouteAttribute>().Select(route => route.Template));
        Assert.Single(type.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Empty(type.GetCustomAttributes<AllowAnonymousAttribute>());
        var exactPath = Path.GetFullPath(documentPage);
        return routedPages.Where(path => !string.Equals(Path.GetFullPath(path), exactPath, StringComparison.Ordinal)).ToArray();
    }
}
