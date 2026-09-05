using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class SourceParityThrough4486f0eTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public SourceParityThrough4486f0eTests(TestingWebApplicationFactory factory)
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

    [Fact]
    public void DailyCheckpoint_AccountsForEveryCommitThroughTheNewSourceHead()
    {
        string checkpoint = Read(FindRepositoryRoot(), "docs", "source-parity-daily-checkpoint.md");

        Assert.Contains("4486f0e964e508e5eb7b43a59eeaec46cc052c67", checkpoint, StringComparison.Ordinal);
        Assert.Contains("Committed source changes after the historical checkpoint: 159", checkpoint, StringComparison.Ordinal);
        Assert.Contains("`c6ac93af03a0dafc506d9570aca96e4aed3b1643`", checkpoint, StringComparison.Ordinal);
        Assert.Contains("`63e5f99f3cef37b1f005b3399333ede53e560587`", checkpoint, StringComparison.Ordinal);
        Assert.Contains("`7b4b2af697207d36a6e7b7784dddefa150193e97`", checkpoint, StringComparison.Ordinal);
        Assert.Contains("`a53b252`, `303f2f1`, `27f340e`", checkpoint, StringComparison.Ordinal);
        Assert.Contains("`736eeb74f53e9f8c58b1d0f5ddabf01d124262ea` (Intranet)", checkpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void DailyManifest_FreezesEverySourceCommitInExactOrderWithoutGaps()
    {
        string manifestPath = Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "source-parity-delta-through-4486f0e.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = document.RootElement;
        JsonElement entries = root.GetProperty("entries");

        Assert.Equal("1.1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("48e628cf7803264bd0b09bfa7a55b15b47e192dd", root.GetProperty("historicalCheckpoint").GetString());
        Assert.Equal("4486f0e964e508e5eb7b43a59eeaec46cc052c67", root.GetProperty("sourceHead").GetString());
        Assert.Equal(159, root.GetProperty("commitCount").GetInt32());
        Assert.Equal(159, entries.GetArrayLength());

        var allowedOwners = root.GetProperty("allowedOwners").EnumerateArray()
            .Select(owner => Assert.IsType<string>(owner.GetString()))
            .ToHashSet(StringComparer.Ordinal);
        var allowedClassifications = root.GetProperty("allowedClassifications").EnumerateArray()
            .Select(classification => Assert.IsType<string>(classification.GetString()))
            .ToHashSet(StringComparer.Ordinal);
        var commits = new List<string>(entries.GetArrayLength());
        var semanticStream = new StringBuilder();
        AppendCanonicalValue(semanticStream, "source-parity-semantic-stream-v1");
        AppendCanonicalValue(semanticStream, Assert.IsType<string>(root.GetProperty("schemaVersion").GetString()));
        AppendCanonicalValue(semanticStream, Assert.IsType<string>(root.GetProperty("historicalCheckpoint").GetString()));
        AppendCanonicalValue(semanticStream, Assert.IsType<string>(root.GetProperty("sourceHead").GetString()));
        AppendCanonicalValue(semanticStream, root.GetProperty("commitCount").GetInt32().ToString());
        AppendCanonicalArray(semanticStream, root.GetProperty("allowedOwners"));
        AppendCanonicalArray(semanticStream, root.GetProperty("allowedClassifications"));
        int expectedSequence = 1;
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            Assert.Equal(expectedSequence++, entry.GetProperty("sequence").GetInt32());
            string commit = Assert.IsType<string>(entry.GetProperty("sourceCommit").GetString());
            Assert.Matches("^[0-9a-f]{40}$", commit);
            commits.Add(commit);

            string classification = Assert.IsType<string>(entry.GetProperty("classification").GetString());
            Assert.Contains(classification, allowedClassifications);
            Assert.DoesNotContain("gap", classification, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pending", classification, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unclassified", classification, StringComparison.OrdinalIgnoreCase);

            string[] owners = [.. entry.GetProperty("owners").EnumerateArray()
                .Select(owner => Assert.IsType<string>(owner.GetString()))];
            Assert.All(owners, owner => Assert.Contains(owner, allowedOwners));
            JsonElement[] targetEvidence = [.. entry.GetProperty("targetEvidence").EnumerateArray()];
            Assert.Equal(owners.Length, targetEvidence.Length);
            Assert.All(targetEvidence, target =>
            {
                string repository = Assert.IsType<string>(target.GetProperty("repository").GetString());
                Assert.Contains(repository, owners);
                Assert.Matches("^[0-9a-f]{40}$", Assert.IsType<string>(target.GetProperty("commit").GetString()));
            });

            int retirementCount = entry.GetProperty("retirements").GetArrayLength();
            int exclusionCount = entry.GetProperty("exclusions").GetArrayLength();
            Assert.True(owners.Length > 0 || retirementCount > 0 || exclusionCount > 0,
                "Every source commit needs a canonical owner, approved retirement, or non-runtime exclusion.");
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("validationEvidence").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("classificationRationale").GetString()));

            AppendCanonicalValue(semanticStream, entry.GetProperty("sequence").GetInt32().ToString());
            AppendCanonicalValue(semanticStream, commit);
            AppendCanonicalValue(semanticStream, Assert.IsType<string>(entry.GetProperty("subject").GetString()));
            AppendCanonicalValue(semanticStream, classification);
            AppendCanonicalArray(semanticStream, entry.GetProperty("owners"));
            AppendCanonicalArray(semanticStream, entry.GetProperty("retirements"));
            AppendCanonicalArray(semanticStream, entry.GetProperty("exclusions"));
            AppendCanonicalArray(semanticStream, entry.GetProperty("sourceAreas"));
            AppendCanonicalValue(semanticStream, targetEvidence.Length.ToString());
            foreach (JsonElement target in targetEvidence)
            {
                AppendCanonicalValue(semanticStream, Assert.IsType<string>(target.GetProperty("repository").GetString()));
                AppendCanonicalValue(semanticStream, Assert.IsType<string>(target.GetProperty("commit").GetString()));
            }

            AppendCanonicalValue(semanticStream, Assert.IsType<string>(entry.GetProperty("validationEvidence").GetString()));
            AppendCanonicalValue(semanticStream, Assert.IsType<string>(entry.GetProperty("classificationRationale").GetString()));
        }

        string sequence = string.Join('\n', commits) + '\n';
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sequence))).ToLowerInvariant();
        Assert.Equal("13fdab8b1cbfd8327e0603ea04d891e38dbbab4dca420803a6d74ff57be9b0da", digest);
        Assert.Equal(digest, root.GetProperty("sequenceSha256").GetString());
        string semanticDigest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(semanticStream.ToString()))).ToLowerInvariant();
        Assert.Equal("5c3c36b5e6b3d224677e7acb1624fab035c585f6cc35de24b7f06ad8576fa0ae", semanticDigest);
        Assert.Equal(semanticDigest, root.GetProperty("semanticSha256").GetString());
        Assert.Equal("25db5545b0f5f575f61616af2ba54ee45e656a20", commits[0]);
        Assert.Equal("4486f0e964e508e5eb7b43a59eeaec46cc052c67", commits[^1]);
    }

    private static string Read(string root, params string[] segments) =>
        File.ReadAllText(Path.Combine([root, .. segments]));

    private static void AppendCanonicalArray(StringBuilder builder, JsonElement values)
    {
        JsonElement[] items = [.. values.EnumerateArray()];
        AppendCanonicalValue(builder, items.Length.ToString());
        foreach (JsonElement item in items)
        {
            AppendCanonicalValue(builder, Assert.IsType<string>(item.GetString()));
        }
    }

    private static void AppendCanonicalValue(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value).Append('\n');

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
