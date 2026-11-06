using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Components.Pages.Member;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberDashboardSurfaceContractTests
{
    [Fact]
    public void MemberDashboardAndAccountRoutes_UseAccessibleAuthenticatedShell()
    {
        var root = FindRepositoryRoot();
        var pages = Path.Combine(root, "Legacy.Maliev.Web", "Areas", "Member", "Pages");
        var dashboard = Path.Combine(pages, "Index.cshtml");
        var dashboardModel = Path.Combine(pages, "Index.cshtml.cs");
        var account = Path.Combine(pages, "Account", "Index.cshtml");
        var accountModel = Path.Combine(pages, "Account", "Index.cshtml.cs");
        var layout = Path.Combine(pages, "Shared", "_LayoutMember.cshtml");
        var navigation = Path.Combine(pages, "Shared", "_MemberNavigation.cshtml");
        var components = Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Member");
        var overviewPage = Path.Combine(components, "MemberOverviewPage.razor");
        var overviewLoader = Path.Combine(components, "MemberOverviewLoader.cs");
        var workspaceShell = Path.Combine(components, "MemberWorkspaceShell.razor");

        Assert.True(File.Exists(dashboard));
        Assert.True(File.Exists(dashboardModel));
        Assert.True(File.Exists(account));
        Assert.True(File.Exists(accountModel));
        Assert.True(File.Exists(layout));
        Assert.True(File.Exists(navigation));
        Assert.True(File.Exists(overviewPage));
        Assert.True(File.Exists(overviewLoader));
        Assert.True(File.Exists(workspaceShell));

        var dashboardSource = File.ReadAllText(dashboardModel);
        var overviewPageSource = File.ReadAllText(overviewPage);
        var overviewLoaderSource = File.ReadAllText(overviewLoader);
        var workspaceShellSource = File.ReadAllText(workspaceShell);
        Assert.Contains("[Authorize]", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("[Authorize]", overviewPageSource, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", overviewPageSource, StringComparison.Ordinal);
        Assert.Contains("GetCustomerDatabaseIdAsync", overviewLoaderSource, StringComparison.Ordinal);
        Assert.Contains("ICustomerAccountClient", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("ICustomerOrderClient", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("ICustomerQuotationClient", dashboardSource, StringComparison.Ordinal);

        var viewStart = File.ReadAllText(Path.Combine(pages, "_ViewStart.cshtml"));
        Assert.Contains("_LayoutMember.cshtml", viewStart, StringComparison.Ordinal);

        var layoutSource = File.ReadAllText(layout);
        var navigationSource = File.ReadAllText(navigation);
        Assert.Contains("href=\"#member-main-content\"", layoutSource, StringComparison.Ordinal);
        Assert.Contains("<main id=\"member-main-content\"", layoutSource, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"member-navigation\"", layoutSource, StringComparison.Ordinal);
        Assert.Contains("event.key === 'Escape'", layoutSource, StringComparison.Ordinal);
        Assert.Contains("<nav", navigationSource, StringComparison.Ordinal);
        Assert.Contains("aria-label", navigationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UserManager", navigationSource, StringComparison.Ordinal);
        Assert.Contains("data-member-workspace", workspaceShellSource, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"member-navigation\"", workspaceShellSource, StringComparison.Ordinal);

        var combined = string.Join('\n', dashboardSource, overviewPageSource, overviewLoaderSource, workspaceShellSource, layoutSource, navigationSource);
        Assert.DoesNotContain("access_token", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DbContext", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paypal", combined, StringComparison.OrdinalIgnoreCase);
    }


    [Theory]
    [InlineData("en", "Description")]
    [InlineData("th", "คำอธิบาย")]
    public async Task OrderHistory_RendersDesktopMultilineDescriptionWithoutChangingOrderLinks(string culture, string descriptionLabel)
    {
        const string description = "First line\n<script>not markup</script>";
        var model = MemberOrderHistoryDisplayModel.Empty with
        {
            Orders =
            [
                new(41, "First order", description, 2, "120.00", "-"),
                new(42, "Second order", null, 1, "60.00", "-"),
            ],
            TotalRecords = 2,
        };
        var html = await RenderAsync<MemberOrderHistoryContent>(culture, new()
        {
            [nameof(MemberOrderHistoryContent.Model)] = model,
        });

        Assert.Contains("<th class=\"d-none d-lg-table-cell\">" + descriptionLabel + "</th>", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.Matches("<td data-title=\"[^\"]+\" class=\"d-none d-lg-table-cell table-multiline\">" + Regex.Escape(HtmlEncoder.Default.Encode(description)) + "</td>", html);
        Assert.Matches("<td data-title=\"[^\"]+\" class=\"d-none d-lg-table-cell table-multiline\">-</td>", html);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/member/orders/view?itemID=41\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/member/orders/view?itemID=42\"", html, StringComparison.Ordinal);
        Assert.Contains("data-stack=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"search\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"sort\"", html, StringComparison.Ordinal);
        Assert.Contains("data-record-count=\"2\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task OrderDetail_RendersMultilineEscapedDescriptionAndRetainsEmptyFallback(string culture)
    {
        const string description = "First line\n<script>not markup</script>";
        var model = MemberOrderDetailDisplayModel.Empty with { Id = 41, Description = description };
        var html = await RenderAsync<MemberOrderDetailContent>(culture, new()
        {
            [nameof(MemberOrderDetailContent.Model)] = model,
        });

        Assert.Matches("<dd class=\"table-multiline\">" + Regex.Escape(HtmlEncoder.Default.Encode(description)) + "</dd>", html);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        var empty = await RenderAsync<MemberOrderDetailContent>(culture, new()
        {
            [nameof(MemberOrderDetailContent.Model)] = model with { Description = null },
        });
        Assert.Matches("<dd class=\"table-multiline\">[^<]+</dd>", empty);
        Assert.DoesNotContain("not markup", empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "missing-both")]
    [InlineData("en", "missing-billing")]
    [InlineData("en", "missing-shipping")]
    [InlineData("en", "complete")]
    [InlineData("en", "unavailable")]
    [InlineData("th", "missing-both")]
    [InlineData("th", "missing-billing")]
    [InlineData("th", "missing-shipping")]
    [InlineData("th", "complete")]
    [InlineData("th", "unavailable")]
    public async Task QuotationAddressWarning_RequiresKnownMissingAddressAndNeverInfersPayment(string culture, string addressState)
    {
        var address = new MemberPostalAddressDisplayModel(["Synthetic address"]);
        var customer = addressState == "unavailable" ? null : new MemberCustomerDisplayModel(
            "Synthetic customer", "synthetic@example.invalid", "-", "-", "-",
            addressState is "complete" or "missing-shipping" ? address : null,
            addressState is "complete" or "missing-billing" ? address : null);
        var expectsWarning = addressState.StartsWith("missing-", StringComparison.Ordinal);
        var warning = culture == "th" ? "ยังไม่มีที่อยู่ออกใบเสร็จหรือจัดส่งของคุณ" : "We don't have your billing or shipping address yet.";
        var action = culture == "th" ? "โปรดระบุที่อยู่ออกใบเสร็จและจัดส่งก่อนดำเนินการต่อ" : "Please let us know where to bill and ship your order before continue.";
        foreach (var invoiceState in new[] { "none", "unpaid", "paid" })
        {
            var invoice = invoiceState == "none" ? null : new MemberInvoiceDisplayModel(
                "SYNTHETIC-41", invoiceState == "paid", MemberDateDisplayModel.Empty, "120.00");
            var model = MemberQuotationDetailDisplayModel.Empty with
            {
                Id = 41,
                Customer = customer,
                Invoice = invoice,
                FreeOnBoard = "SYNTHETIC-FOB",
                Errors = ["Some quotation details are temporarily unavailable."],
            };
            var html = await RenderAsync<MemberQuotationDetailContent>(culture, new()
            {
                [nameof(MemberQuotationDetailContent.Model)] = model,
            });
            var decoded = WebUtility.HtmlDecode(html);
            Assert.Equal(expectsWarning, html.Contains("data-member-address-warning", StringComparison.Ordinal));
            if (expectsWarning)
            {
                Assert.Contains(warning, decoded, StringComparison.Ordinal);
                Assert.Contains(action, decoded, StringComparison.Ordinal);
                Assert.Contains("href=\"/member/account/manage/address\"", html, StringComparison.Ordinal);
                Assert.Matches("role=\"status\"[^>]*data-member-address-warning", html);
            }
            else
            {
                Assert.DoesNotContain(warning, decoded, StringComparison.Ordinal);
                Assert.DoesNotContain(action, decoded, StringComparison.Ordinal);
            }

            var paymentLabel = (culture, invoiceState) switch
            {
                ("th", "none") => "ยังไม่มีการออกใบแจ้งหนี้",
                ("th", "unpaid") => "รอการชำระเงิน",
                ("th", "paid") => "ชำระแล้ว",
                (_, "none") => "No invoice has been issued.",
                (_, "unpaid") => "Awaiting payment",
                _ => "Paid",
            };
            Assert.Contains(paymentLabel, decoded, StringComparison.Ordinal);
            Assert.Contains("<dt>" + (culture == "th" ? "เงื่อนไขส่งมอบ" : "Free on board") + "</dt><dd>SYNTHETIC-FOB</dd>", decoded, StringComparison.Ordinal);
            Assert.DoesNotContain("Payment confirmed", decoded, StringComparison.Ordinal);
            Assert.DoesNotContain("<form", html, StringComparison.Ordinal);
            Assert.DoesNotContain("paypal", html, StringComparison.OrdinalIgnoreCase);
            if (customer is null)
            {
                Assert.Contains(culture == "th" ? "ไม่มีข้อมูลลูกค้า" : "Customer details are not available.", decoded, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task MemberShell_ContainsChildFormInResponsiveContentSurfaceNotNavigationOrToolbar(string culture)
    {
        RenderFragment content = builder =>
        {
            builder.OpenElement(0, "form");
            builder.AddAttribute(1, "method", "post");
            builder.AddAttribute(2, "action", "/Member/Account/Manage/Profile?handler=UpdateProfile");
            builder.OpenElement(3, "input");
            builder.AddAttribute(4, "name", "__RequestVerificationToken");
            builder.AddAttribute(5, "value", "synthetic-token");
            builder.CloseElement();
            builder.CloseElement();
        };
        var html = await RenderAsync<MemberWorkspaceShell>(culture, new()
        {
            [nameof(MemberWorkspaceShell.ChildContent)] = content,
        });

        Assert.Matches("<main[^>]*id=\"member-main-content\"[^>]*>.*?<div class=\"workspace-toolbar\">.*?</div>\\s*<div class=\"workspace-content\">\\s*<form", html.Replace("\n", string.Empty, StringComparison.Ordinal));
        Assert.Contains("action=\"/Member/Account/Manage/Profile?handler=UpdateProfile\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"member-navigation\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", html[..html.IndexOf("<main", StringComparison.Ordinal)], StringComparison.Ordinal);
        var css = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "application-shell.css"));
        Assert.Matches(@"\.workspace-content\s*\{[^}]*display:\s*grid;[^}]*min-width:\s*0;", css);
        Assert.Matches(@"\.table-multiline\s*\{[^}]*white-space:\s*pre-wrap;[^}]*overflow-wrap:\s*anywhere;", css);
    }

    private static async Task<string> RenderAsync<TComponent>(string culture, Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var services = new ServiceCollection()
                .AddLogging()
                .AddLocalization(options => options.ResourcesPath = "Resources")
                .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() })
                .BuildServiceProvider();
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            return await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters))).ToHtmlString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
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
