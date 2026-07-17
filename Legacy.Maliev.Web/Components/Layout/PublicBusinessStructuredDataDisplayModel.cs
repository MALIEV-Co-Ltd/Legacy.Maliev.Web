using System.Globalization;
using System.Text.Json;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Components.Layout;

public sealed record PublicBusinessStructuredDataDisplayModel(
    string OrganizationJson,
    string LocalBusinessJson)
{
    private static readonly string[] SocialLinks =
    [
        SocialNetworks.Facebook,
        SocialNetworks.Instagram,
        SocialNetworks.Line,
        SocialNetworks.YouTube,
        SocialNetworks.TikTok,
        SocialNetworks.Threads
    ];

    public static PublicBusinessStructuredDataDisplayModel Create()
    {
        var isThai = string.Equals(
            CultureInfo.CurrentCulture.TwoLetterISOLanguageName,
            "th",
            StringComparison.OrdinalIgnoreCase);
        var organization = CreateOrganization(isThai);
        var localBusiness = CreateLocalBusiness(isThai);

        return new PublicBusinessStructuredDataDisplayModel(
            JsonSerializer.Serialize(organization),
            JsonSerializer.Serialize(localBusiness));
    }

    private static Dictionary<string, object> CreateOrganization(bool isThai) => new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "Organization",
        ["@id"] = "https://www.maliev.com/#organization",
        ["name"] = "Maliev Co., Ltd.",
        ["alternateName"] = "MALIEV Manufacturing",
        ["url"] = "https://www.maliev.com",
        ["logo"] = new Dictionary<string, object>
        {
            ["@type"] = "ImageObject",
            ["url"] = "https://www.maliev.com/src/images/navbar_logo_black.png",
            ["width"] = 653,
            ["height"] = 150
        },
        ["description"] = isThai
            ? "บริษัทรับผลิตชิ้นส่วนตามแบบ CNC Machining, 3D Printing, 3D Scanning ด้วยเครื่องจักรที่ทันสมัย"
            : "Manufacturing company specializing in CNC machining, 3D printing, and 3D scanning services with modern equipment.",
        ["foundingDate"] = "2018-01",
        ["founders"] = new[]
        {
            CreatePerson("Natthapol Vanasrivilai"),
            CreatePerson("Thossapol Vanasrivilai"),
            CreatePerson("Natthakarn Vanasrivilai")
        },
        ["address"] = CreateAddress(),
        ["contactPoint"] = new[]
        {
            CreateContactPoint("+66818030404", "customer service", "info@maliev.com", includeEmail: true),
            CreateContactPoint("+66898950690", "sales", "manufacturing@maliev.com", includeEmail: true)
        },
        ["sameAs"] = SocialLinks,
        ["taxID"] = "0125561001573"
    };

    private static Dictionary<string, object> CreateLocalBusiness(bool isThai) => new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "LocalBusiness",
        ["@id"] = "https://www.maliev.com/#organization",
        ["name"] = "Maliev Co., Ltd.",
        ["image"] = "https://www.maliev.com/src/images/navbar_logo_black.png",
        ["logo"] = "https://www.maliev.com/src/images/navbar_logo_black.png",
        ["url"] = "https://www.maliev.com",
        ["hasMap"] = SocialNetworks.GoogleMaps,
        ["telephone"] = "+66818030404",
        ["priceRange"] = "$$",
        ["address"] = CreateAddress(),
        ["geo"] = new Dictionary<string, object>
        {
            ["@type"] = "GeoCoordinates",
            ["latitude"] = 13.9469417,
            ["longitude"] = 100.4588118
        },
        ["openingHoursSpecification"] = new[]
        {
            new Dictionary<string, object>
            {
                ["@type"] = "OpeningHoursSpecification",
                ["dayOfWeek"] = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" },
                ["opens"] = "09:00",
                ["closes"] = "18:00"
            }
        },
        ["sameAs"] = SocialLinks,
        ["contactPoint"] = CreateContactPoint("+66818030404", "customer service", string.Empty, includeEmail: false),
        ["description"] = isThai
            ? "บริการรับผลิตชิ้นงานตามแบบ งาน CNC งานพิมพ์ 3 มิติ งานสแกน 3 มิติ และ Reverse Engineering จากปากเกร็ด นนทบุรี พร้อมประเมินโครงการออนไลน์"
            : "Custom part manufacturing, CNC machining, 3D printing, 3D scanning, and reverse-engineering services from Pak Kret, Nonthaburi, with online project review.",
        ["email"] = "info@maliev.com",
        ["foundingDate"] = "2018-01",
        ["paymentAccepted"] = "Cash, Credit Card, Bank Transfer",
        ["currenciesAccepted"] = "THB"
    };

    private static Dictionary<string, object> CreatePerson(string name) => new()
    {
        ["@type"] = "Person",
        ["name"] = name
    };

    private static Dictionary<string, object> CreateAddress() => new()
    {
        ["@type"] = "PostalAddress",
        ["streetAddress"] = "36/1 Moo 3, Khlong Khoi",
        ["addressLocality"] = "Pak Kret",
        ["addressRegion"] = "Nonthaburi",
        ["postalCode"] = "11120",
        ["addressCountry"] = "TH"
    };

    private static Dictionary<string, object> CreateContactPoint(
        string telephone,
        string contactType,
        string email,
        bool includeEmail)
    {
        var contactPoint = new Dictionary<string, object>
        {
            ["@type"] = "ContactPoint",
            ["telephone"] = telephone,
            ["contactType"] = contactType,
            ["areaServed"] = "TH",
            ["availableLanguage"] = new[] { "Thai", "English" }
        };

        if (includeEmail)
        {
            contactPoint["email"] = email;
        }

        return contactPoint;
    }
}
