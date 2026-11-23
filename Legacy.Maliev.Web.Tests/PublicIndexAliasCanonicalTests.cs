using System.Globalization;
using Legacy.Maliev.Web.Components.Layout;
using Microsoft.AspNetCore.Http;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicIndexAliasCanonicalTests
{
    [Theory]
    [InlineData("/", "/Index")]
    [InlineData("/about", "/About/Index")]
    [InlineData("/legal", "/Legal/Index")]
    [InlineData("/account", "/Account/Index")]
    [InlineData("/contact", "/Contact/Index")]
    [InlineData("/quotation", "/Quotation/Index")]
    [InlineData("/career", "/Career/Index")]
    [InlineData("/services", "/Services/Index")]
    [InlineData("/knowledges", "/Knowledges/Index")]
    [InlineData("/knowledges/specifications", "/Knowledges/Specifications/Index")]
    public void IndexAlias_UsesShortRouteForCanonicalAndAlternates(string shortPath, string alias)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in new[] { "en", "th" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                var shortContext = Context(shortPath, culture);
                var aliasContext = Context(alias, culture);
                Assert.Equal(alias, CanonicalUrlPolicy.GetCanonicalPath(aliasContext.Request.Path));
                Assert.Equal(PublicDocumentLinksDisplayModel.Create(shortContext), PublicDocumentLinksDisplayModel.Create(aliasContext));
                Assert.Equal(
                    PublicOpenGraphMetadataDisplayModel.Create(shortContext, null, null, null).Url,
                    PublicOpenGraphMetadataDisplayModel.Create(aliasContext, null, null, null).Url);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    private static DefaultHttpContext Context(string path, string culture)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString($"?culture={culture}");
        return context;
    }
}
