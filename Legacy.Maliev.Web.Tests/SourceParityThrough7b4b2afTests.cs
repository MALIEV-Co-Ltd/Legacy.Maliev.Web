using System.Net;
using System.Text.Json;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class SourceParityThrough7b4b2afTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public SourceParityThrough7b4b2afTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("en", "Add MALIEV on LINE", "Use the button or QR code to open MALIEV on LINE.")]
    [InlineData("th", "เพิ่มเพื่อน MALIEV บน LINE", "ใช้ปุ่มหรือคิวอาร์โค้ดเพื่อเปิด MALIEV บน LINE")]
    public async Task LineFriendshipRoute_IsLocalizedNoindexAndFailsSafeWithoutLiff(
        string culture,
        string heading,
        string fallback)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync($"/contact/line?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<h1 id=\"line-title\">{heading}</h1>", source, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"robots\" content=\"noindex,follow\"", source, StringComparison.Ordinal);
        var serializedFallback = JsonSerializer.Serialize(fallback);
        Assert.Contains($"'{serializedFallback[1..^1]}'", source, StringComparison.Ordinal);
        Assert.Contains("line_friend_confirmed", source, StringComparison.Ordinal);
        Assert.Contains("friendship.friendFlag === true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("user_id", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("static.line-scdn.net/liff/edge/2/sdk.js", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnedLineRoute_IsClassifiedAndLinkedFromPublicContactSurfaces()
    {
        string root = FindRepositoryRoot();
        string analytics = Read(root, "Legacy.Maliev.Web", "Components", "Analytics", "PublicContactChannelAnalytics.razor");
        string contact = Read(root, "Legacy.Maliev.Web", "Components", "Pages", "Contact", "ContactPage.razor");
        string location = Read(root, "Legacy.Maliev.Web", "Components", "Shared", "ServiceLocation.razor");

        Assert.Contains("path === '/contact/line'", analytics, StringComparison.Ordinal);
        Assert.Contains("href=\"/contact/line\"", contact, StringComparison.Ordinal);
        Assert.Contains("We accept customer files throughout Thailand", location, StringComparison.Ordinal);
        Assert.Contains("ship completed parts nationwide by parcel", location, StringComparison.Ordinal);
        Assert.Contains("schedule an appointment before visiting", location, StringComparison.Ordinal);
        Assert.Contains("รับไฟล์งานจากลูกค้าทั่วประเทศไทย", location, StringComparison.Ordinal);
        Assert.Contains("จัดส่งชิ้นงานสำเร็จทั่วประเทศทางพัสดุ", location, StringComparison.Ordinal);
    }

    [Fact]
    public void ServicePages_PreserveConfirmedReverseEngineeringTravelAndInstallationOwnership()
    {
        string root = FindRepositoryRoot();
        string scanning = Read(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "ThreeDimensionalScanningContent.razor");
        string injection = Read(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "LowVolumeInjectionMoldingContent.razor");
        string injectionPage = Read(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "LowVolumeInjectionMoldingPage.razor");

        Assert.Contains("replacement parts", scanning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/services/cnc-machining\"", scanning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/services/custom-manufacturing\"", scanning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("very large, cannot be disassembled, or cannot reasonably be shipped", scanning, StringComparison.Ordinal);
        Assert.Contains("Travel is quoted by distance and project scope", scanning, StringComparison.Ordinal);
        Assert.Contains("ชิ้นงานขนาดใหญ่มาก ถอดประกอบไม่ได้ หรือไม่สะดวกจัดส่ง", scanning, StringComparison.Ordinal);
        Assert.Contains("installed at the customer location", injection, StringComparison.Ordinal);
        Assert.Contains("Outside the Bangkok metropolitan area, installation and travel are quoted by distance", injection, StringComparison.Ordinal);
        Assert.Contains("ติดตั้งเครื่อง PIMM ณ สถานที่ของลูกค้า", injection, StringComparison.Ordinal);
        Assert.Contains("Do you install a purchased PIMM machine at our site?", injectionPage, StringComparison.Ordinal);
    }

    [Fact]
    public void LiffSdk_IsAllowlistedWithoutWideningOtherCspSources()
    {
        string root = FindRepositoryRoot();
        string policy = Read(root, "Legacy.Maliev.Web", "Middleware", "WebContentSecurityPolicyMiddleware.cs");

        Assert.Contains("https://static.line-scdn.net", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src *", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("connect-src *", policy, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", true, false)]
    [InlineData("2000000000-AbCd_1234", true, true)]
    [InlineData("javascript:alert(1)", false, false)]
    [InlineData("2000000000-../../escape", false, false)]
    public void LineLiffOptions_AcceptsOnlyBoundedPublicIdentifiers(
        string liffId,
        bool isValid,
        bool isEnabled)
    {
        var options = new LineLiffOptions { LiffId = liffId };

        Assert.Equal(isValid, options.IsValid);
        Assert.Equal(isEnabled, options.IsEnabled);
    }

    [Fact]
    public async Task ConfiguredLineFriendshipRoute_LoadsOnlyTheAllowlistedLiffSdk()
    {
        using var configuredFactory = factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"{LineLiffOptions.SectionName}:LiffId",
            "2000000000-AbCd_1234"));
        using var client = configuredFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/contact/line?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("https://static.line-scdn.net/liff/edge/2/sdk.js", source, StringComparison.Ordinal);
        Assert.Contains("2000000000-AbCd_1234", source, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string root, params string[] segments) =>
        File.ReadAllText(Path.Combine([root, .. segments]));

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
