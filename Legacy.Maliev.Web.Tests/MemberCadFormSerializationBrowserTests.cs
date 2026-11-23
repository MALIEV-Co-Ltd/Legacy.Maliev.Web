using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberCadFormSerializationBrowserTests
{
    [Theory]
    [InlineData("3d-printing", "additive")]
    [InlineData("3d-scanning", "scanning")]
    [InlineData("cnc-machining", "machining")]
    public async Task NativeMemberPost_PreservesCadPartsAndRenderedOperation(string orderPath, string orderKind)
    {
        await using var host = new MemberOrderUploadBrowserTests.MemberOrderBrowserHost(formOnly: true);
        await host.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = MemberOrderUploadBrowserTests.MemberOrderBrowserHost.AuthenticationScheme,
            },
            ServiceWorkers = ServiceWorkerPolicy.Block,
        });

        var origin = new Uri(host.Origin);
        var targetPath = $"/member/orders/{orderPath}";
        var target = new Uri(origin, targetPath + "?culture=en&process=Synthetic%20process&material=Synthetic%20material");
        var capture = new TaskCompletionSource<IRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            var url = new Uri(request.Url);
            var isLocal = url.GetLeftPart(UriPartial.Authority) == origin.GetLeftPart(UriPartial.Authority);
            if (isLocal && request.Method is "GET" or "HEAD")
            {
                await route.ContinueAsync();
                return;
            }

            await route.AbortAsync();
            if (isLocal && request.Method == "POST" && request.ResourceType == "document")
            {
                capture.TrySetResult(request);
            }
        });
        await context.RouteWebSocketAsync("**/*", route => _ = route.CloseAsync());
        await using var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15_000);
        var response = await page.GotoAsync(target.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(200, response?.Status);
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var form = page.Locator("form[data-terms-gated-form]:has([data-member-order-form])");
        await Assertions.Expect(form.Locator("[data-member-order-form]")).ToHaveAttributeAsync("data-kind", orderKind);
        var renderedOperation = await form.Locator("input[name=OperationId]").InputValueAsync();
        Assert.True(Guid.TryParse(renderedOperation, out var operationId) && operationId != Guid.Empty);
        var verification = await form.Locator("input[name=__RequestVerificationToken]").InputValueAsync();
        Assert.False(string.IsNullOrWhiteSpace(verification));

        byte[] stl = WallThicknessParityBrowserFixture.Box(1);
        byte[] step = Encoding.ASCII.GetBytes("ISO-10303-21;\r\nEND-ISO-10303-21;");
        await form.Locator("#member-order-files").SetInputFilesAsync(
        [
            new FilePayload { Name = "synthetic-part.stl", MimeType = "application/sla", Buffer = stl },
            new FilePayload { Name = "synthetic-part.step", MimeType = "", Buffer = step },
        ]);
        await form.Locator("#member-order-name").FillAsync("Synthetic CAD request");
        if (orderKind == "scanning")
        {
            await form.Locator("#member-order-width").FillAsync("1");
            await form.Locator("#member-order-length").FillAsync("2");
            await form.Locator("#member-order-height").FillAsync("3");
        }
        else
        {
            await form.Locator("#member-order-quantity").FillAsync("2");
            await form.Locator("#member-order-finish").SelectOptionAsync("15");
            if (orderKind == "additive") await form.Locator("#member-order-color").SelectOptionAsync("14");
        }

        await form.Locator("[data-terms-checkbox]").CheckAsync();
        var submit = form.Locator("[data-terms-submit]");
        var action = new Uri(await submit.EvaluateAsync<string>("button => button.hasAttribute('formaction') ? button.formAction : button.form.action"));
        Assert.Equal(origin.GetLeftPart(UriPartial.Authority), action.GetLeftPart(UriPartial.Authority));
        Assert.Equal(targetPath, action.AbsolutePath, ignoreCase: true);
        Assert.Equal("Submit", QueryHelpers.ParseQuery(action.Query)["handler"].ToString());
        Assert.True(await form.EvaluateAsync<bool>("form => form.checkValidity()"));
        await submit.ClickAsync();
        var request = await capture.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(action, new Uri(request.Url));

        var mediaType = MediaTypeHeaderValue.Parse(request.Headers["content-type"]);
        Assert.Equal("multipart/form-data", mediaType.MediaType.Value);
        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        Assert.False(string.IsNullOrWhiteSpace(boundary));
        var body = request.PostDataBuffer;
        Assert.True(body is { Length: > 0 and <= 32_768 }, "The captured synthetic multipart body must be small and nonempty.");
        using var stream = new MemoryStream(body!);
        var reader = new MultipartReader(boundary!, stream) { BodyLengthLimit = 16_384 };
        var fields = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var files = new Dictionary<string, (string ContentType, byte[] Bytes)>(StringComparer.Ordinal);
        while (await reader.ReadNextSectionAsync() is { } section)
        {
            var disposition = ContentDispositionHeaderValue.Parse(section.ContentDisposition);
            Assert.Equal("form-data", disposition.DispositionType.Value);
            var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value!;
            if (disposition.IsFileDisposition())
            {
                Assert.Equal("Files", name);
                using var part = new MemoryStream();
                await section.Body.CopyToAsync(part);
                files.Add(HeaderUtilities.RemoveQuotes(disposition.FileName).Value!, (section.ContentType!, part.ToArray()));
            }
            else
            {
                Assert.True(disposition.IsFormDisposition());
                using var value = new StreamReader(section.Body, Encoding.UTF8);
                if (!fields.TryGetValue(name, out var entries)) fields.Add(name, entries = []);
                entries.Add(await value.ReadToEndAsync());
            }
        }

        Assert.Equal(2, files.Count);
        AssertFile("synthetic-part.stl", "application/sla", stl);
        AssertFile("synthetic-part.step", "application/octet-stream", step);
        Assert.True(fields.TryGetValue("OperationId", out var operations)
            && operations.Count == 1 && Guid.TryParse(operations[0], out var postedOperation) && postedOperation == operationId);
        Assert.True(fields.TryGetValue("__RequestVerificationToken", out var tokens)
            && tokens.Count == 1 && tokens[0] == verification);
        Assert.Equal(["true", "false"], fields["AcceptTermsAndConditions"]);
        Assert.Equal("Synthetic CAD request", Assert.Single(fields["Name"]));
        if (orderKind == "scanning")
        {
            Assert.Equal("1", Assert.Single(fields["Width"]));
            Assert.Equal("2", Assert.Single(fields["Length"]));
            Assert.Equal("3", Assert.Single(fields["Height"]));
            Assert.False(fields.ContainsKey("ProcessId"));
        }
        else
        {
            Assert.Equal("2", Assert.Single(fields["Quantity"]));
            Assert.Equal("11", Assert.Single(fields["ProcessId"]));
            Assert.Equal("13", Assert.Single(fields["MaterialId"]));
            Assert.Equal("15", Assert.Single(fields["SurfaceFinishId"]));
            if (orderKind == "additive") Assert.Equal("14", Assert.Single(fields["ColorId"]));
            else Assert.False(fields.ContainsKey("ColorId"));
        }
        Assert.Equal(0, host.RequestsBlockedAtHost);
        Assert.Equal(0, host.SubmissionCalls);

        void AssertFile(string name, string contentType, byte[] expected)
        {
            var actual = files[name];
            Assert.Equal(contentType, actual.ContentType);
            Assert.True(expected.AsSpan().SequenceEqual(actual.Bytes), "The native file part changed the synthetic fixture bytes.");
        }
    }
}
