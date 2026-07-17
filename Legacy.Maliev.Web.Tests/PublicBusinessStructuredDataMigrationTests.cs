using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicBusinessStructuredDataMigrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public PublicBusinessStructuredDataMigrationTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"))
            .CreateClient();
    }

    [Fact]
    public void SharedPublicLayouts_UseDisplayOnlyStaticBusinessStructuredDataComponent()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var layoutPaths = new[]
        {
            Path.Combine(web, "Pages", "Shared", "_Layout.cshtml"),
            Path.Combine(web, "Pages", "Shared", "_InstantQuotationLayout.cshtml")
        };

        foreach (var layoutPath in layoutPaths)
        {
            var layout = File.ReadAllText(layoutPath);
            Assert.Contains("type=\"typeof(PublicBusinessStructuredData)\"", layout, StringComparison.Ordinal);
            Assert.Contains("render-mode=\"Static\"", layout, StringComparison.Ordinal);
            Assert.Contains("PublicBusinessStructuredDataDisplayModel.Create()", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("_SchemaOrganization", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("_SchemaLocalBusiness", layout, StringComparison.Ordinal);
        }

        var componentPath = Path.Combine(web, "Components", "Layout", "PublicBusinessStructuredData.razor");
        var modelPath = Path.Combine(web, "Components", "Layout", "PublicBusinessStructuredDataDisplayModel.cs");
        Assert.True(File.Exists(componentPath));
        Assert.True(File.Exists(modelPath));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_SchemaOrganization.cshtml")));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_SchemaLocalBusiness.cshtml")));

        var component = File.ReadAllText(componentPath);
        Assert.Contains("data-migration-component=\"public-business-structured-data\"", component, StringComparison.Ordinal);
        Assert.Contains("Model.OrganizationJson", component, StringComparison.Ordinal);
        Assert.Contains("Model.LocalBusinessJson", component, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpContext", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        var model = File.ReadAllText(modelPath);
        Assert.Contains("JsonSerializer.Serialize", model, StringComparison.Ordinal);
        Assert.Contains("SocialNetworks.GoogleMaps", model, StringComparison.Ordinal);
        Assert.DoesNotContain("Html.Raw", model, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", model, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", model, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "Manufacturing company specializing in CNC machining", "Custom part manufacturing, CNC machining")]
    [InlineData("th", "บริษัทรับผลิตชิ้นส่วนตามแบบ", "บริการรับผลิตชิ้นงานตามแบบ")]
    public async Task BusinessStructuredData_RendersParseableLocalizedSemanticContracts(
        string culture,
        string organizationDescriptionPrefix,
        string localBusinessDescriptionPrefix)
    {
        using var response = await client.GetAsync($"/legal?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var schemas = ExtractSchemas(source);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-component=\"public-business-structured-data\"", source, StringComparison.Ordinal);
        Assert.Equal(2, schemas.Count);

        var organization = schemas["Organization"].RootElement;
        Assert.Equal("https://schema.org", organization.GetProperty("@context").GetString());
        Assert.Equal("https://www.maliev.com/#organization", organization.GetProperty("@id").GetString());
        Assert.Equal("Maliev Co., Ltd.", organization.GetProperty("name").GetString());
        Assert.Equal("MALIEV Manufacturing", organization.GetProperty("alternateName").GetString());
        Assert.Equal("https://www.maliev.com", organization.GetProperty("url").GetString());
        Assert.StartsWith(organizationDescriptionPrefix, organization.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Equal("2018-01", organization.GetProperty("foundingDate").GetString());
        Assert.Equal("0125561001573", organization.GetProperty("taxID").GetString());

        var logo = organization.GetProperty("logo");
        Assert.Equal("ImageObject", logo.GetProperty("@type").GetString());
        Assert.Equal("https://www.maliev.com/src/images/navbar_logo_black.png", logo.GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Number, logo.GetProperty("width").ValueKind);
        Assert.Equal(653, logo.GetProperty("width").GetInt32());
        Assert.Equal(JsonValueKind.Number, logo.GetProperty("height").ValueKind);
        Assert.Equal(150, logo.GetProperty("height").GetInt32());
        var logoDimensions = ReadPngDimensions(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "wwwroot",
            "src",
            "images",
            "navbar_logo_black.png"));
        Assert.Equal(logoDimensions.Width, logo.GetProperty("width").GetInt32());
        Assert.Equal(logoDimensions.Height, logo.GetProperty("height").GetInt32());

        var founders = organization.GetProperty("founders").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Natthapol Vanasrivilai", "Thossapol Vanasrivilai", "Natthakarn Vanasrivilai" },
            founders.Select(founder => founder.GetProperty("name").GetString()));
        Assert.All(founders, founder => Assert.Equal("Person", founder.GetProperty("@type").GetString()));
        AssertAddress(organization.GetProperty("address"));

        var organizationContacts = organization.GetProperty("contactPoint").EnumerateArray().ToArray();
        Assert.Equal(2, organizationContacts.Length);
        Assert.Equal("+66818030404", organizationContacts[0].GetProperty("telephone").GetString());
        Assert.Equal("info@maliev.com", organizationContacts[0].GetProperty("email").GetString());
        Assert.Equal("+66898950690", organizationContacts[1].GetProperty("telephone").GetString());
        Assert.Equal("manufacturing@maliev.com", organizationContacts[1].GetProperty("email").GetString());
        AssertSocialLinks(organization.GetProperty("sameAs"));

        var localBusiness = schemas["LocalBusiness"].RootElement;
        Assert.Equal("https://schema.org", localBusiness.GetProperty("@context").GetString());
        Assert.Equal("https://www.maliev.com/#organization", localBusiness.GetProperty("@id").GetString());
        Assert.Equal("Maliev Co., Ltd.", localBusiness.GetProperty("name").GetString());
        Assert.Equal("https://www.maliev.com", localBusiness.GetProperty("url").GetString());
        Assert.Equal(SocialNetworks.GoogleMaps, localBusiness.GetProperty("hasMap").GetString());
        Assert.Equal("+66818030404", localBusiness.GetProperty("telephone").GetString());
        Assert.Equal("$$", localBusiness.GetProperty("priceRange").GetString());
        Assert.StartsWith(localBusinessDescriptionPrefix, localBusiness.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Equal("info@maliev.com", localBusiness.GetProperty("email").GetString());
        Assert.Equal("Cash, Credit Card, Bank Transfer", localBusiness.GetProperty("paymentAccepted").GetString());
        Assert.Equal("THB", localBusiness.GetProperty("currenciesAccepted").GetString());
        AssertAddress(localBusiness.GetProperty("address"));
        Assert.Equal(13.9469417, localBusiness.GetProperty("geo").GetProperty("latitude").GetDouble(), 7);
        Assert.Equal(100.4588118, localBusiness.GetProperty("geo").GetProperty("longitude").GetDouble(), 7);
        Assert.Equal("09:00", localBusiness.GetProperty("openingHoursSpecification")[0].GetProperty("opens").GetString());
        Assert.Equal("18:00", localBusiness.GetProperty("openingHoursSpecification")[0].GetProperty("closes").GetString());
        Assert.Equal(5, localBusiness.GetProperty("openingHoursSpecification")[0].GetProperty("dayOfWeek").GetArrayLength());
        AssertSocialLinks(localBusiness.GetProperty("sameAs"));
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, JsonDocument> ExtractSchemas(string source) => Regex.Matches(
            source,
            "<script[^>]*type=\"application/ld\\+json\"[^>]*>(?<json>[\\s\\S]*?)</script>",
            RegexOptions.CultureInvariant)
        .Select(match => JsonDocument.Parse(match.Groups["json"].Value))
        .ToDictionary(document => document.RootElement.GetProperty("@type").GetString()!, StringComparer.Ordinal);

    private static void AssertAddress(JsonElement address)
    {
        Assert.Equal("PostalAddress", address.GetProperty("@type").GetString());
        Assert.Equal("36/1 Moo 3, Khlong Khoi", address.GetProperty("streetAddress").GetString());
        Assert.Equal("Pak Kret", address.GetProperty("addressLocality").GetString());
        Assert.Equal("Nonthaburi", address.GetProperty("addressRegion").GetString());
        Assert.Equal("11120", address.GetProperty("postalCode").GetString());
        Assert.Equal("TH", address.GetProperty("addressCountry").GetString());
    }

    private static void AssertSocialLinks(JsonElement sameAs)
    {
        Assert.Equal(
            new[]
            {
                SocialNetworks.Facebook,
                SocialNetworks.Instagram,
                SocialNetworks.Line,
                SocialNetworks.YouTube,
                SocialNetworks.TikTok,
                SocialNetworks.Threads
            },
            sameAs.EnumerateArray().Select(link => link.GetString()));
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        var header = File.ReadAllBytes(path).AsSpan();
        Assert.True(header.Length >= 24, "The logo PNG header is incomplete.");

        return (
            BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
