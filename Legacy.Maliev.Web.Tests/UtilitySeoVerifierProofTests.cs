using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Components.Pages.Member;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Runs actual offline verifier code and separately identifies controlled component-only proof.</summary>
public sealed class UtilitySeoVerifierProofTests
{
    [Fact]
    public async Task ActualPowerShellVerifier_UsesOwnedLoopbackAndFailsClosedWithoutPrerequisite()
    {
        var root = FindRoot();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(root, "tests", "UtilitySeoVerifierProof.ps1") })
            process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
            var output = await stdout;
            var error = await stderr;
            Assert.True(process.ExitCode == 0, $"Offline verifier failed: {output} {error}");
            using var report = JsonDocument.Parse(output.Trim());
            Assert.Equal(9, report.RootElement.GetProperty("passed").GetInt32());
            Assert.Equal(0, report.RootElement.GetProperty("failed").GetInt32());
            Assert.Equal("owned-loopback-only", report.RootElement.GetProperty("transport").GetString());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ControlledMemberComponent_EmitsSingleMetadataWithoutClaimingAuthenticatedHttp(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var services = new ServiceCollection().AddLogging().AddLocalization(options => options.ResourcesPath = "Resources")
                .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() })
                .AddSingleton<IJSRuntime, UnexpectedJavaScript>().BuildServiceProvider();
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var head = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var outlet = await renderer.RenderComponentAsync<HeadOutlet>();
                var page = await renderer.RenderComponentAsync<MemberAccountIndexPage>();
                Assert.Contains("member-account-index-content", page.ToHtmlString(), StringComparison.Ordinal);
                return outlet.ToHtmlString();
            });
            var tags = Regex.Matches(WebUtility.HtmlDecode(head), "<meta\\b[^>]*>", RegexOptions.IgnoreCase)
                .Where(tag => tag.Value.Contains("name=\"robots\"", StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.Contains("content=\"noindex,follow\"", Assert.Single(tags).Value, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private sealed class UnexpectedJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Static component proof must not invoke JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new InvalidOperationException("Static component proof must not invoke JavaScript.");
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Owned Web workspace was not found.");
    }
}
