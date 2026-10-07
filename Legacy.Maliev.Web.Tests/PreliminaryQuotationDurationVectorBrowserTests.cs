using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Exercises the original finite duration vector through the public production popup API.</summary>
[Collection(PublicContactBrowserCollection.Name)]
public sealed class PreliminaryQuotationDurationVectorBrowserTests(PublicContactBrowserFixture fixture)
{
    /// <summary>Retains the original minute vector with explicitly reviewed English display adaptation and exact Thai fields.</summary>
    /// <param name="culture">The original supported language.</param>
    /// <returns>The owned browser verification.</returns>
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task OriginalMinuteVector_RendersExactLocalizedPopupFields(string culture)
    {
        double[] minutes = [1, 60, 917.4, 1501];
        string[] expected = culture == "th"
            ? ["1 นาที", "1 ชม.", "15 ชม. 17 นาที", "1 วัน 1 ชม. 1 นาที"]
            : ["1 minute", "1 hour", "15 hours 17 minutes", "1 day 1 hour 1 minute"];
        var text = SnapshotLabels(culture);
        var snapshot = new
        {
            locale = culture, currency = culture == "th" ? "บาท" : "THB", logoDataUri = "", text,
            parts = minutes.Select((value, index) => new
            {
                partId = $"synthetic-duration-{index + 1}", partNumber = index + 1,
                fileName = $"duration-vector-{index + 1:00}.stl", material = "PLA", color = "White",
                buildPreference = "Standard", quantity = 1, unitPrice = 600, subtotal = 600,
                technicalFilamentMinimumApplied = false, technicalFilamentMinimumAdjustment = 0,
                printTimeMinutes = value, dimensionXmm = 10, dimensionYmm = 10, dimensionZmm = 10,
                volumeCm3 = 1, surfaceAreaCm2 = 6, minThicknessMm = 1, dfmWarnings = Array.Empty<string>(),
            }).ToArray(),
            totals = new { subtotal = 2400, minimumOrderPrice = 600, minimumOrderSurcharge = 0,
                shipping = 100, vat = 175, total = 2675, leadTimeMinimumDays = 1, leadTimeMaximumDays = 3 },
        };
        await using IBrowserContext context = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 1280, Height = 900 }, Locale = culture });
        IPage page = await context.NewPageAsync();
        int pageErrors = 0;
        page.PageError += (_, _) => Interlocked.Increment(ref pageErrors);
        IPage? popup = null;
        try
        {
            var response = await page.GotoAsync(new Uri(fixture.Origin,
                $"/instantquotation/3d-printing?culture={culture}").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.NotNull(response);
            Assert.Equal(200, response.Status);
            string html = await response.TextAsync();
            Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", html, StringComparison.Ordinal);
            Assert.Contains("/dist/route-instant-quotation.js?v=", html, StringComparison.Ordinal);
            var root = BrowserHostIdentityVerifier.SourceProjectDirectory();
            byte[] asset = File.ReadAllBytes(Path.Combine(root, "wwwroot", "dist", "route-instant-quotation.js"));
            var servedAsset = await context.APIRequest.GetAsync(new Uri(fixture.Origin,
                "/dist/route-instant-quotation.js").ToString());
            Assert.Equal(200, servedAsset.Status);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(asset)),
                Convert.ToHexString(SHA256.HashData(await servedAsset.BodyAsync())));
            await page.WaitForFunctionAsync("typeof window.malievPreliminaryQuotation?.open === 'function'");
            popup = await page.RunAndWaitForPopupAsync(() => page.EvaluateAsync(
                "snapshot => window.malievPreliminaryQuotation.open(snapshot)", snapshot));
            popup.PageError += (_, _) => Interlocked.Increment(ref pageErrors);
            await popup.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await Assertions.Expect(popup.Locator("article.iq-preliminary-quotation-part")).ToHaveCountAsync(4);
            for (int index = 0; index < minutes.Length; index++)
            {
                ILocator article = popup.Locator("article.iq-preliminary-quotation-part").Nth(index);
                Assert.Equal($"duration-vector-{index + 1:00}.stl", await article.Locator("h2").InnerTextAsync());
                var matches = new List<ILocator>();
                foreach (ILocator field in await article.Locator(
                    ":scope > .iq-preliminary-quotation-part-body > .iq-preliminary-quotation-fields > .iq-preliminary-quotation-field").AllAsync())
                {
                    if (await field.Locator("dt").InnerTextAsync() == text["printTime"]) matches.Add(field);
                }
                ILocator duration = Assert.Single(matches);
                Assert.Equal(expected[index], await duration.Locator("dd").InnerTextAsync());
            }
            Assert.Equal(0, pageErrors);
        }
        finally
        {
            if (popup is not null && !popup.IsClosed) await popup.CloseAsync();
        }
    }

    private Dictionary<string, string> SnapshotLabels(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            string component = File.ReadAllText(Path.Combine(BrowserHostIdentityVerifier.SourceProjectDirectory(),
                "Components", "Pages", "InstantQuotation", "InstantQuotationPreliminaryQuotation.razor"));
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(component,
                """\["(?<key>[^"]+)"\] = Localizer\["(?<value>[^"]+)"\]\.Value"""))
            {
                labels.Add(match.Groups["key"].Value,
                    fixture.PreliminaryQuotationLocalizer[match.Groups["value"].Value].Value);
            }
            foreach (string key in new[] { "day", "days", "hour", "hours", "minute", "minutes", "printTime" })
                Assert.True(labels.ContainsKey(key));
            Assert.Equal(culture == "th" ? "เวลาพิมพ์โดยประมาณต่อชิ้น" : "Estimated print time per part", labels["printTime"]);
            return labels;
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
