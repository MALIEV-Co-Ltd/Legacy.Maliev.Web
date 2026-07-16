using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuotationPage = Legacy.Maliev.Web.Pages.Quotation.Index;

namespace Legacy.Maliev.Web.Tests;

public sealed class QuotationPageTests
{
    [Theory]
    [InlineData("3d-printing", "3d_printing")]
    [InlineData("3D-Scanning", "3d_scanning")]
    [InlineData("cnc-machining", "cnc_machining")]
    [InlineData("injection-molding", "injection_molding")]
    [InlineData("unsupported", "custom_manufacturing")]
    [InlineData(null, "custom_manufacturing")]
    public async Task Get_NormalizesServiceContextToControlledAnalyticsValue(
        string? item,
        string expectedService)
    {
        var page = CreatePage(
            new RecordingQuotationClient(),
            new RecordingFileClient(),
            new StubAntiBotVerifier(true));

        await page.OnGetAsync("en", item, null, null, CancellationToken.None);

        Assert.Equal(expectedService, page.ServiceContext);
    }

    [Fact]
    public async Task Post_InvalidAntiBotTokenNeverPersistsRequest()
    {
        var quotation = new RecordingQuotationClient();
        var page = CreatePage(quotation, new RecordingFileClient(), new StubAntiBotVerifier(false));

        var result = await page.OnPostSubmitRequestAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0, quotation.CallCount);
        Assert.False(page.ModelState.IsValid);
    }

    [Fact]
    public async Task Post_OversizedFilesAreRejectedBeforeRequestPersistence()
    {
        var quotation = new RecordingQuotationClient();
        var page = CreatePage(quotation, new RecordingFileClient(), new StubAntiBotVerifier(true));
        page.Files = [new StubFormFile("oversized.step", 100L * 1024L * 1024L + 1L)];

        var result = await page.OnPostSubmitRequestAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0, quotation.CallCount);
        Assert.Contains(
            page.ModelState[nameof(page.Files)]!.Errors,
            error => error.ErrorMessage.Contains("100 MB", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_PersistedRequestWithUploadFailureRedirectsWithReferenceAndNoResubmitMessage()
    {
        var submissionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var quotation = new RecordingQuotationClient(new QuotationRequestResult(713, true, true));
        var files = new RecordingFileClient(new QuotationFileResult(false, false, true, false));
        var notifications = new RecordingNotificationClient();
        var page = CreatePage(quotation, files, new StubAntiBotVerifier(true), notifications);
        page.SubmissionId = submissionId;
        page.Files = [FormFile("model.stl", "model/stl", "solid test")];

        var result = await page.OnPostSubmitRequestAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Index", redirect.PageName);
        Assert.Equal("legacy-web-quotation-11111111222233334444555555555555", quotation.IdempotencyKey);
        Assert.Equal(713, files.RequestId);
        Assert.Contains("#713", page.Notification, StringComparison.Ordinal);
        Assert.Contains("Do not submit", page.Notification, StringComparison.OrdinalIgnoreCase);
        var analytics = Assert.Single(page.TempData.Values.OfType<string>(), value => value.Contains("713", StringComparison.Ordinal));
        using var analyticsDocument = JsonDocument.Parse(analytics);
        var analyticsEvent = analyticsDocument.RootElement;
        Assert.Equal("request_quote", analyticsEvent.GetProperty("event").GetString());
        Assert.Equal("quotation_request", analyticsEvent.GetProperty("intent_type").GetString());
        Assert.Equal("cnc_machining", analyticsEvent.GetProperty("service").GetString());
        Assert.Equal("quotation-713", analyticsEvent.GetProperty("transaction_id").GetString());
        Assert.Equal("persisted", analyticsEvent.GetProperty("submission_status").GetString());
        Assert.True(analyticsEvent.GetProperty("has_files").GetBoolean());
        Assert.False(analyticsEvent.GetProperty("file_upload_completed").GetBoolean());
        Assert.DoesNotContain("mali@example.com", analytics, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, notifications.Messages.Count);
    }

    [Fact]
    public async Task Post_CompletedUploadQueuesCompletionAndNormalizesTamperedService()
    {
        var quotation = new RecordingQuotationClient(new QuotationRequestResult(714, true, true));
        var files = new RecordingFileClient(new QuotationFileResult(true, true, true, false));
        var page = CreatePage(quotation, files, new StubAntiBotVerifier(true));
        page.Files = [FormFile("model.step", "application/step", "STEP")];
        page.ServiceContext = "untrusted-service";

        await page.OnPostSubmitRequestAsync(CancellationToken.None);

        var analytics = Assert.Single(
            page.TempData.Values.OfType<string>(),
            value => value.Contains("714", StringComparison.Ordinal));
        using var analyticsDocument = JsonDocument.Parse(analytics);
        var analyticsEvent = analyticsDocument.RootElement;
        Assert.Equal("custom_manufacturing", analyticsEvent.GetProperty("service").GetString());
        Assert.True(analyticsEvent.GetProperty("has_files").GetBoolean());
        Assert.True(analyticsEvent.GetProperty("file_upload_completed").GetBoolean());
    }

    private static QuotationPage CreatePage(
        RecordingQuotationClient quotationClient,
        RecordingFileClient fileClient,
        IAntiBotVerifier antiBotVerifier,
        INotificationClient? notificationClient = null)
    {
        var httpContext = new DefaultHttpContext();
        return new QuotationPage(
            new StubCountryClient(),
            quotationClient,
            fileClient,
            notificationClient ?? new RecordingNotificationClient(),
            antiBotVerifier,
            Options.Create(
                new RecaptchaEnterpriseOptions
                {
                    SiteKey = "test-site-key",
                    ProjectId = "test-project"
                }),
            NullLogger<QuotationPage>.Instance)
        {
            PageContext = new PageContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new MemoryTempDataProvider()),
            SubmissionId = Guid.NewGuid(),
            ServiceContext = "cnc_machining",
            FirstName = "Mali",
            LastName = "Ev",
            Email = "mali@example.com",
            Country = "Thailand",
            Message = "Please quote these parts",
            RecaptchaToken = "browser-token"
        };
    }

    private static IFormFile FormFile(string fileName, string contentType, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(
                new ServiceResponse<IReadOnlyList<Country>>(
                    [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)],
                    true));
    }

    private sealed class RecordingQuotationClient(
        QuotationRequestResult? result = null) : IQuotationClient
    {
        public int CallCount { get; private set; }

        public string? IdempotencyKey { get; private set; }

        public Task<QuotationRequestResult> CreateRequestAsync(
            QuotationRequestSubmission submission,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            CallCount++;
            IdempotencyKey = idempotencyKey;
            return Task.FromResult(result ?? new QuotationRequestResult(null, true, true));
        }
    }

    private sealed class RecordingFileClient(
        QuotationFileResult? result = null) : IQuotationFileClient
    {
        public int? RequestId { get; private set; }

        public Task<QuotationFileResult> UploadAndLinkAsync(
            int requestId,
            Guid submissionId,
            IReadOnlyList<QuotationUpload> files,
            CancellationToken cancellationToken)
        {
            RequestId = requestId;
            return Task.FromResult(result ?? new QuotationFileResult(true, true, true, false));
        }
    }

    private sealed class StubAntiBotVerifier(bool valid) : IAntiBotVerifier
    {
        public Task<bool> VerifyAsync(
            string? token,
            string expectedAction,
            CancellationToken cancellationToken)
        {
            Assert.Equal("submit", expectedAction);
            return Task.FromResult(valid);
        }
    }

    private sealed class RecordingNotificationClient : INotificationClient
    {
        public List<EmailNotification> Messages { get; } = [];

        public Task<NotificationResult> SendAsync(
            NotificationChannel channel,
            EmailNotification notification,
            CancellationToken cancellationToken)
        {
            Messages.Add(notification);
            return Task.FromResult(new NotificationResult(true, true, true));
        }
    }

    private sealed class StubFormFile(string fileName, long length) : IFormFile
    {
        public string ContentType => "application/octet-stream";

        public string ContentDisposition => string.Empty;

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public long Length => length;

        public string Name => "Files";

        public string FileName => fileName;

        public void CopyTo(Stream target) => throw new NotSupportedException();

        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Stream OpenReadStream() => throw new NotSupportedException();
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> values = new Dictionary<string, object>();

        public IDictionary<string, object> LoadTempData(HttpContext context) => values;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => this.values = values;
    }
}
