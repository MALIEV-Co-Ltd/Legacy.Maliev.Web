using Legacy.Maliev.Web.Components.Pages.Quotation;
using Microsoft.Extensions.Localization;
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

        await page.OnGetAsync(
            "en",
            item,
            null,
            null,
            finder_files: null,
            finder_service: null,
            finder_material: null,
            finder_quantity: null,
            finder_end_use: null,
            finder_performance: null,
            finder_environment: null,
            finder_recommendations: null,
            finder_path: null,
            finish_hex: null,
            finish_hlc: null,
            finish_lab: null,
            finish_pantone: null,
            finish_sheen: null,
            cancellationToken: CancellationToken.None);

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
    public async Task Post_MoreThanTenFilesWithinAggregateLimitAreAccepted()
    {
        var quotation = new RecordingQuotationClient(new QuotationRequestResult(715, true, true));
        var files = new RecordingFileClient(new QuotationFileResult(true, true, true, false));
        var page = CreatePage(quotation, files, new StubAntiBotVerifier(true));
        page.Files = Enumerable.Range(1, 11)
            .Select(index => FormFile($"drawing-{index}.step", "application/step", "STEP"))
            .ToArray();

        var result = await page.OnPostSubmitRequestAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, quotation.CallCount);
        Assert.Equal(11, files.UploadCount);
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
        Assert.Equal("maliev_lead_submitted", analyticsEvent.GetProperty("event").GetString());
        Assert.Equal("manual_quote", analyticsEvent.GetProperty("lead_type").GetString());
        Assert.Equal("cnc_machining", analyticsEvent.GetProperty("service").GetString());
        Assert.Equal("request-713", analyticsEvent.GetProperty("transaction_id").GetString());
        Assert.Equal("persisted", analyticsEvent.GetProperty("lead_status").GetString());
        Assert.True(analyticsEvent.GetProperty("has_files").GetBoolean());
        Assert.False(analyticsEvent.TryGetProperty("file_upload_completed", out _));
        Assert.DoesNotContain("mali@example.com", analytics, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, notifications.Messages.Count);
    }

    [Theory]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, false)]
    [InlineData(false, 1, false)]
    [InlineData(false, 2, false)]
    [InlineData(true, 2, true)]
    [InlineData(false, 1, true)]
    public async Task Post_PersistedRequestKeepsReferenceWhenNotificationThrows(
        bool synchronousFailure,
        int failingCall,
        bool dependencyCancellation)
    {
        var quotation = new RecordingQuotationClient(new QuotationRequestResult(713, true, true));
        var files = new RecordingFileClient(new QuotationFileResult(true, true, true, false));
        var healthyDelivery = new TaskCompletionSource<NotificationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothDeliveriesStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = new RecordingNotificationClient((call, _) =>
        {
            if (call == 2)
            {
                bothDeliveriesStarted.TrySetResult(true);
            }

            if (call != failingCall)
            {
                return healthyDelivery.Task;
            }

            Exception failure = dependencyCancellation
                ? new OperationCanceledException("private-notification-failure")
                : new InvalidOperationException("private-notification-failure");
            if (synchronousFailure)
            {
                throw failure;
            }

            return dependencyCancellation
                ? Task.FromCanceled<NotificationResult>(new CancellationToken(canceled: true))
                : Task.FromException<NotificationResult>(failure);
        });
        var page = CreatePage(quotation, files, new StubAntiBotVerifier(true), notifications);
        page.Files = [FormFile("model.step", "application/step", "STEP")];

        var pending = page.OnPostSubmitRequestAsync(CancellationToken.None);
        try
        {
            await Task.WhenAny(pending, bothDeliveriesStarted.Task).WaitAsync(TimeSpan.FromSeconds(10));
            var observedHealthyDelivery = bothDeliveriesStarted.Task.IsCompleted && !pending.IsCompleted;
            healthyDelivery.TrySetResult(new NotificationResult(true, true, true));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("Index", redirect.PageName);
            Assert.Equal(1, quotation.CallCount);
            Assert.Equal(713, files.RequestId);
            Assert.Equal(2, notifications.Messages.Count);
            Assert.True(observedHealthyDelivery);
            Assert.Contains("#713", page.Notification, StringComparison.Ordinal);
            Assert.Contains("Do not submit", page.Notification, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-notification-failure", page.Notification, StringComparison.Ordinal);
            var analytics = Assert.Single(page.TempData.Values.OfType<string>(), value => value.Contains("713", StringComparison.Ordinal));
            using var document = JsonDocument.Parse(analytics);
            Assert.Equal("maliev_lead_submitted", document.RootElement.GetProperty("event").GetString());
            Assert.Equal("request-713", document.RootElement.GetProperty("transaction_id").GetString());
            Assert.True(document.RootElement.GetProperty("has_files").GetBoolean());
            Assert.DoesNotContain("private-notification-failure", analytics, StringComparison.Ordinal);
        }
        finally
        {
            healthyDelivery.TrySetResult(new NotificationResult(true, true, true));
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory]
    [InlineData("synchronous")]
    [InlineData("asynchronous")]
    [InlineData("stream")]
    [InlineData("foreign-cancellation")]
    public async Task Post_PersistedRequestKeepsQueuedReferenceWhenAttachmentFollowUpThrows(string failureMode)
    {
        var quotation = new RecordingQuotationClient(new QuotationRequestResult(713, true, true));
        var files = new RecordingFileClient(upload: (uploads, _) =>
        {
            if (failureMode == "stream")
            {
                using var stream = uploads.Single().OpenReadStream();
                return Task.FromResult(new QuotationFileResult(true, true, true, false));
            }

            if (failureMode == "foreign-cancellation")
            {
                return Task.FromCanceled<QuotationFileResult>(new CancellationToken(canceled: true));
            }

            var failure = new IOException("private-attachment-failure");
            if (failureMode == "synchronous")
            {
                throw failure;
            }

            return Task.FromException<QuotationFileResult>(failure);
        });
        var notifications = new RecordingNotificationClient();
        var page = CreatePage(quotation, files, new StubAntiBotVerifier(true), notifications);
        page.Files = failureMode == "stream"
            ? [new StubFormFile("unreadable.step", 4)]
            : [FormFile("model.step", "application/step", "STEP")];

        var result = await page.OnPostSubmitRequestAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Index", redirect.PageName);
        Assert.Equal(1, quotation.CallCount);
        Assert.Equal(713, files.RequestId);
        Assert.Empty(notifications.Messages);
        Assert.Contains("#713", page.Notification, StringComparison.Ordinal);
        Assert.Contains("Do not submit", page.Notification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-attachment-failure", page.Notification, StringComparison.Ordinal);
        var analytics = Assert.Single(page.TempData.Values.OfType<string>(), value => value.Contains("713", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(analytics);
        Assert.Equal("request-713", document.RootElement.GetProperty("transaction_id").GetString());
        Assert.True(document.RootElement.GetProperty("has_files").GetBoolean());
        Assert.DoesNotContain("private-attachment-failure", analytics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_PersistedRequestPropagatesCancellationDuringAttachmentFollowUp()
    {
        using var requestAbort = new CancellationTokenSource();
        var quotation = new RecordingQuotationClient(new QuotationRequestResult(713, true, true));
        var files = new RecordingFileClient(upload: (_, cancellationToken) =>
        {
            requestAbort.Cancel();
            return Task.FromCanceled<QuotationFileResult>(cancellationToken);
        });
        var notifications = new RecordingNotificationClient();
        var page = CreatePage(quotation, files, new StubAntiBotVerifier(true), notifications);
        page.Files = [FormFile("model.step", "application/step", "STEP")];

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => page.OnPostSubmitRequestAsync(requestAbort.Token));

        Assert.Equal(1, quotation.CallCount);
        Assert.Empty(notifications.Messages);
        Assert.Null(page.Notification);
        Assert.Single(page.TempData.Values.OfType<string>(), value => value.Contains("request-713", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_PersistedRequestPropagatesCancellationAfterBothNotificationsStart()
    {
        using var requestAbort = new CancellationTokenSource();
        var quotation = new RecordingQuotationClient(new QuotationRequestResult(713, true, true));
        var files = new RecordingFileClient();
        var notifications = new RecordingNotificationClient((call, cancellationToken) =>
        {
            if (call == 1)
            {
                requestAbort.Cancel();
                return Task.FromCanceled<NotificationResult>(cancellationToken);
            }

            return Task.FromResult(new NotificationResult(true, true, true));
        });
        var page = CreatePage(quotation, files, new StubAntiBotVerifier(true), notifications);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => page.OnPostSubmitRequestAsync(requestAbort.Token));

        Assert.Equal(1, quotation.CallCount);
        Assert.Equal(2, notifications.Messages.Count);
        Assert.Null(page.Notification);
        Assert.Single(page.TempData.Values.OfType<string>(), value => value.Contains("request-713", StringComparison.Ordinal));
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
        Assert.False(analyticsEvent.TryGetProperty("file_upload_completed", out _));
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
            new AnonymousSessionManager(),
            new UnusedCustomerAccountClient(),
            notificationClient ?? new RecordingNotificationClient(),
            antiBotVerifier,
            Options.Create(
                new RecaptchaEnterpriseOptions
                {
                    SiteKey = "test-site-key",
                    ProjectId = "test-project"
                }),
            new StringLocalizer<QuotationContent>(new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }), NullLoggerFactory.Instance)),
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
            var value = result ?? new QuotationRequestResult(null, true, true);
            return Task.FromResult(value.ReferenceNumber is int id
                ? value with
                {
                    TransactionId = value.TransactionId ?? $"request-{id}",
                    JourneyId = value.JourneyId ?? submission.JourneyId,
                }
                : value);
        }
    }

    private sealed class RecordingFileClient(
        QuotationFileResult? result = null,
        Func<IReadOnlyList<QuotationUpload>, CancellationToken, Task<QuotationFileResult>>? upload = null) : IQuotationFileClient
    {
        public int? RequestId { get; private set; }
        public int UploadCount { get; private set; }

        public Task<QuotationFileResult> UploadAndLinkAsync(
            int requestId,
            Guid submissionId,
            IReadOnlyList<QuotationUpload> files,
            CancellationToken cancellationToken)
        {
            RequestId = requestId;
            UploadCount = files.Count;
            return upload?.Invoke(files, cancellationToken)
                ?? Task.FromResult(result ?? new QuotationFileResult(true, true, true, false));
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

    private sealed class AnonymousSessionManager : IAccountSessionManager
    {
        public Task<AccountSignInStatus> SignInAsync(
            HttpContext context,
            string email,
            string password,
            bool rememberMe,
            CancellationToken cancellationToken) =>
            Task.FromResult(AccountSignInStatus.InvalidCredentials);

        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<int?>(null);
    }

    private sealed class UnusedCustomerAccountClient : ICustomerAccountClient
    {
        public Task<CustomerAddressProfileResult> GetAddressProfileAsync(int customerId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Anonymous quotation tests must not load a customer profile.");

        public Task<CustomerAddressOperationResult> UpdateAddressesAsync(int customerId, CustomerAddressUpdate update, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Anonymous quotation tests must not update customer data.");

        public Task<CustomerAddressOperationResult> UpdateEmailAsync(int customerId, string email, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Anonymous quotation tests must not update customer data.");

        public Task<CustomerAccountProfileResult> GetProfileAsync(int customerId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Anonymous quotation tests must not load a customer profile.");

        public Task<CustomerAddressOperationResult> UpdateProfileAsync(int customerId, CustomerProfileUpdate update, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Anonymous quotation tests must not update customer data.");
    }

    private sealed class RecordingNotificationClient(
        Func<int, CancellationToken, Task<NotificationResult>>? send = null) : INotificationClient
    {
        public List<EmailNotification> Messages { get; } = [];

        public Task<NotificationResult> SendAsync(
            NotificationChannel channel,
            EmailNotification notification,
            CancellationToken cancellationToken)
        {
            Messages.Add(notification);
            return send?.Invoke(Messages.Count, cancellationToken)
                ?? Task.FromResult(new NotificationResult(true, true, true));
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
