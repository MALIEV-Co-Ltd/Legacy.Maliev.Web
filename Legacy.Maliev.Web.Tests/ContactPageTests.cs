using Legacy.Maliev.Web.Components.Pages.Contact;
using Microsoft.Extensions.Localization;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using System.Text.Json;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ContactPage = Legacy.Maliev.Web.Pages.Contact.Index;

namespace Legacy.Maliev.Web.Tests;

public sealed class ContactPageTests
{
    [Theory]
    [InlineData("en", "Thank you for contacting us. Your reference number is #913.")]
    [InlineData("th", "ขอบคุณที่ติดต่อเรา หมายเลขอ้างอิงของคุณคือ #913")]
    public async Task Post_AnalyticsSetterFailureStillAcknowledgesPersistedContactAndSendsBothNotifications(
        string culture, string expectedAcknowledgement)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var tempData = new LeadAnalyticsEventQueueParityTests.ThrowingTempDataDictionary();
            var logger = new LeadAnalyticsEventQueueParityTests.RecordingLogger<ContactPage>();
            var contact = new RecordingContactClient(new ContactSubmissionResult(913, true, true));
            var notifications = new RecordingNotificationClient((_, _) =>
            {
                Assert.Equal(1, tempData.SetterCalls);
                return Task.FromResult(new NotificationResult(true, true, true));
            });
            var page = CreatePage(contact, new StubAntiBotVerifier(true), notifications, logger: logger);
            page.TempData = tempData;

            var result = await page.OnPostSubmitRequestAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("Index", redirect.PageName);
            Assert.Equal(culture, redirect.RouteValues!["culture"]);
            Assert.Equal(1, contact.CallCount);
            Assert.Equal(2, notifications.Messages.Count);
            Assert.Equal(1, tempData.SetterCalls);
            Assert.Equal(0, tempData.GetterCalls);
            Assert.Equal(0, tempData.SaveCalls);
            Assert.True(page.ModelState.IsValid);
            Assert.Equal(expectedAcknowledgement, page.Notification);
            Assert.DoesNotContain("private-tempdata-setter-fault", page.Notification, StringComparison.Ordinal);
            using var attempted = JsonDocument.Parse(Assert.IsType<string>(tempData.AttemptedValue));
            Assert.Equal("maliev_contact_submitted", attempted.RootElement.GetProperty("event").GetString());
            Assert.Equal("message-913", attempted.RootElement.GetProperty("transaction_id").GetString());
            var warning = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Null(warning.Exception);
            Assert.Equal("Analytics queue failed after contact message 913 was persisted.", warning.Message);
            Assert.DoesNotContain("private-tempdata-setter-fault", warning.Message, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public async Task Post_InvalidAntiBotTokenNeverCallsContactService()
    {
        var contactClient = new RecordingContactClient();
        var page = CreatePage(contactClient, new StubAntiBotVerifier(false));

        var result = await page.OnPostSubmitRequestAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0, contactClient.CallCount);
        Assert.False(page.ModelState.IsValid);
        Assert.Contains(
            page.ModelState[string.Empty]!.Errors,
            error => error.ErrorMessage.Contains("verification", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Post_PersistedContactQueuesReferenceOnlyAndRedirects()
    {
        var contactClient = new RecordingContactClient(
            new ContactSubmissionResult(913, true, true));
        var notifications = new RecordingNotificationClient();
        var page = CreatePage(contactClient, new StubAntiBotVerifier(true), notifications);
        page.Message = "<script>alert('email')</script>";

        var result = await page.OnPostSubmitRequestAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Index", redirect.PageName);
        Assert.Equal("en", redirect.RouteValues!["culture"]);
        Assert.Equal(1, contactClient.CallCount);
        var analyticsPayload = Assert.Single(page.TempData.Values.OfType<string>(), value => value.Contains("913", StringComparison.Ordinal));
        using var analyticsDocument = JsonDocument.Parse(analyticsPayload);
        var analyticsEvent = analyticsDocument.RootElement;
        Assert.Equal("maliev_contact_submitted", analyticsEvent.GetProperty("event").GetString());
        Assert.Equal("contact", analyticsEvent.GetProperty("lead_type").GetString());
        Assert.Equal("general_contact", analyticsEvent.GetProperty("service").GetString());
        Assert.Equal("message-913", analyticsEvent.GetProperty("transaction_id").GetString());
        Assert.Equal("persisted", analyticsEvent.GetProperty("lead_status").GetString());
        Assert.False(analyticsEvent.GetProperty("has_files").GetBoolean());
        Assert.False(analyticsEvent.TryGetProperty("file_upload_completed", out _));
        Assert.DoesNotContain("mali@example.com", analyticsPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Please contact me", analyticsPayload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, notifications.Messages.Count);
        Assert.All(
            notifications.Messages,
            message => Assert.DoesNotContain("<script>", message.Body, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "&lt;script&gt;alert(&#39;email&#39;)&lt;/script&gt;",
            notifications.Messages[1].Body,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, false)]
    [InlineData(false, 1, false)]
    [InlineData(false, 2, false)]
    [InlineData(true, 2, true)]
    [InlineData(false, 1, true)]
    public async Task Post_PersistedContactKeepsReferenceWhenNotificationThrows(
        bool synchronousFailure,
        int failingCall,
        bool dependencyCancellation)
    {
        var contactClient = new RecordingContactClient(new ContactSubmissionResult(913, true, true));
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
        var logger = new FollowUpLogger();
        var page = CreatePage(contactClient, new StubAntiBotVerifier(true), notifications, logger: logger);

        var pending = page.OnPostSubmitRequestAsync(CancellationToken.None);
        try
        {
            await Task.WhenAny(pending, bothDeliveriesStarted.Task).WaitAsync(TimeSpan.FromSeconds(10));
            var observedHealthyDelivery = bothDeliveriesStarted.Task.IsCompleted && !pending.IsCompleted;
            healthyDelivery.TrySetResult(new NotificationResult(true, true, true));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("Index", redirect.PageName);
            Assert.Equal("en", redirect.RouteValues!["culture"]);
            Assert.Equal(1, contactClient.CallCount);
            Assert.Equal(2, notifications.Messages.Count);
            Assert.True(observedHealthyDelivery);
            Assert.Contains("#913", page.Notification, StringComparison.Ordinal);
            Assert.Contains("Do not submit", page.Notification, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-notification-failure", page.Notification, StringComparison.Ordinal);
            var analytics = Assert.Single(page.TempData.Values.OfType<string>(), value => value.Contains("913", StringComparison.Ordinal));
            using var document = JsonDocument.Parse(analytics);
            Assert.Equal("maliev_contact_submitted", document.RootElement.GetProperty("event").GetString());
            Assert.Equal("message-913", document.RootElement.GetProperty("transaction_id").GetString());
            Assert.DoesNotContain("private-notification-failure", analytics, StringComparison.Ordinal);
            AssertFollowUpFailure(
                logger,
                913,
                failingCall == 1 ? "customer_notification" : "internal_notification",
                dependencyCancellation
                    ? synchronousFailure ? nameof(OperationCanceledException) : nameof(TaskCanceledException)
                    : nameof(InvalidOperationException),
                "private-notification-failure");
        }
        finally
        {
            healthyDelivery.TrySetResult(new NotificationResult(true, true, true));
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Post_PersistedContactPropagatesRequestCancellationAfterBothNotificationsStart()
    {
        using var requestAbort = new CancellationTokenSource();
        var contactClient = new RecordingContactClient(new ContactSubmissionResult(913, true, true));
        var notifications = new RecordingNotificationClient((call, cancellationToken) =>
        {
            if (call == 1)
            {
                requestAbort.Cancel();
                return Task.FromCanceled<NotificationResult>(cancellationToken);
            }

            return Task.FromResult(new NotificationResult(true, true, true));
        });
        var logger = new FollowUpLogger();
        var page = CreatePage(contactClient, new StubAntiBotVerifier(true), notifications, logger: logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => page.OnPostSubmitRequestAsync(requestAbort.Token));

        Assert.Equal(1, contactClient.CallCount);
        Assert.Equal(2, notifications.Messages.Count);
        Assert.Null(page.Notification);
        Assert.Empty(logger.Entries);
        Assert.Single(page.TempData.Values.OfType<string>(), value => value.Contains("message-913", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_AuthenticatedCustomerReplacesSpoofedBrowserIdentityWithTrustedProfile()
    {
        var contactClient = new RecordingContactClient(new ContactSubmissionResult(914, true, true));
        var trustedCustomer = new ContactTrustedCustomer(
            "Trusted",
            "Customer",
            "trusted@example.com",
            "+66810000000",
            "Trusted Company",
            "Thailand");
        var page = CreatePage(
            contactClient,
            new StubAntiBotVerifier(true),
            trustedCustomerLoader: new StubTrustedCustomerLoader(new(trustedCustomer, true, true)));
        page.FirstName = "Spoofed";
        page.LastName = "Browser";
        page.Email = "attacker@example.com";
        page.Phone = "+66000000000";
        page.Company = "Attacker Company";
        page.Country = "Nowhere";

        var result = await page.OnPostSubmitRequestAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var submission = Assert.Single(contactClient.Submissions);
        Assert.Equal("Trusted", submission.FirstName);
        Assert.Equal("Customer", submission.LastName);
        Assert.Equal("trusted@example.com", submission.Email);
        Assert.Equal("+66810000000", submission.Telephone);
        Assert.Equal("Trusted Company", submission.Company);
        Assert.Equal("Thailand", submission.Country);
    }

    [Fact]
    public async Task Post_AuthenticatedCustomerProfileFailureFailsClosed()
    {
        var contactClient = new RecordingContactClient(new ContactSubmissionResult(915, true, true));
        var page = CreatePage(
            contactClient,
            new StubAntiBotVerifier(true),
            trustedCustomerLoader: new StubTrustedCustomerLoader(new(null, true, false)));

        var result = await page.OnPostSubmitRequestAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Empty(contactClient.Submissions);
        Assert.Contains(
            page.ModelState[string.Empty]!.Errors,
            error => error.ErrorMessage.Contains("profile", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("en", "Please enter a valid email address")]
    [InlineData("th", "กรุณากรอกอีเมลที่ถูกต้อง")]
    public async Task Post_InvalidEmailPreservesOriginalLocalizedMessageWithoutWriting(
        string culture, string expected)
    {
        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var contactClient = new RecordingContactClient();
            var notifications = new RecordingNotificationClient();
            var page = CreatePage(contactClient, new StubAntiBotVerifier(true), notifications);
            page.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
            page.Email = "not-an-email";
            var validation = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateProperty(
                page.Email,
                new System.ComponentModel.DataAnnotations.ValidationContext(page) { MemberName = nameof(page.Email) },
                validation));
            var error = Assert.Single(validation);
            Assert.Equal("Please enter a valid email address", error.ErrorMessage);
            page.ModelState.AddModelError(nameof(page.Email), error.ErrorMessage!);

            Assert.IsType<PageResult>(await page.OnPostSubmitRequestAsync(CancellationToken.None));
            Assert.Equal(expected, Assert.Single(page.DisplayModel.ErrorsFor(nameof(page.Email))));
            Assert.Equal(0, contactClient.CallCount);
            Assert.Empty(notifications.Messages);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static ContactPage CreatePage(
        RecordingContactClient contactClient,
        IAntiBotVerifier antiBotVerifier,
        INotificationClient? notificationClient = null,
        IContactTrustedCustomerLoader? trustedCustomerLoader = null,
        ILogger<ContactPage>? logger = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?culture=en");
        httpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "customer:27")], "test"));
        var page = new ContactPage(
            new StubCountryClient(),
            contactClient,
            notificationClient ?? new RecordingNotificationClient(),
            antiBotVerifier,
            Options.Create(
                new RecaptchaEnterpriseOptions
                {
                    SiteKey = "test-site-key",
                    ProjectId = "test-project"
                }),
            Options.Create(new GoogleMapsOptions()),
            trustedCustomerLoader ?? new StubTrustedCustomerLoader(new(null, false, true)),
            new StringLocalizer<ContactContent>(new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }), NullLoggerFactory.Instance)),
            logger ?? NullLogger<ContactPage>.Instance)
        {
            PageContext = new PageContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new MemoryTempDataProvider()),
            FirstName = "Mali",
            LastName = "Ev",
            Email = "mali@example.com",
            Country = "Thailand",
            Message = "Please contact me",
            RecaptchaToken = "browser-token"
        };
        return page;
    }

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ServiceResponse<IReadOnlyList<Country>>(
                    [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)],
                    true));
    }

    private sealed class RecordingContactClient(
        ContactSubmissionResult? result = null) : IContactClient
    {
        public int CallCount { get; private set; }
        public List<ContactSubmission> Submissions { get; } = [];

        public Task<ContactSubmissionResult> SubmitAsync(
            ContactSubmission submission,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Submissions.Add(submission);
            return Task.FromResult(result ?? new ContactSubmissionResult(null, true, true));
        }
    }

    private sealed class StubTrustedCustomerLoader(ContactTrustedCustomerLoadResult result)
        : IContactTrustedCustomerLoader
    {
        public Task<ContactTrustedCustomerLoadResult> LoadAsync(
            HttpContext context,
            IReadOnlyList<Country> countries,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class StubAntiBotVerifier(bool valid) : IAntiBotVerifier
    {
        public Task<bool> VerifyAsync(
            string? token,
            string expectedAction,
            CancellationToken cancellationToken)
        {
            Assert.Equal("submit", expectedAction);
            Assert.Equal("browser-token", token);
            return Task.FromResult(valid);
        }
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

    private static void AssertFollowUpFailure(
        FollowUpLogger logger,
        int referenceNumber,
        string stage,
        string exceptionType,
        string sensitive)
    {
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal(3, entry.Properties.Count);
        Assert.Equal(referenceNumber, entry.Properties["MessageId"]);
        Assert.Equal(stage, entry.Properties["Stage"]);
        Assert.Equal(exceptionType, entry.Properties["ExceptionType"]);
        Assert.DoesNotContain(sensitive, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mali@example.com", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("browser-token", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitive, JsonSerializer.Serialize(entry.Properties), StringComparison.Ordinal);
    }

    private sealed class FollowUpLogger : ILogger<ContactPage>
    {
        public System.Collections.Concurrent.ConcurrentQueue<FollowUpLogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.Where(pair => pair.Key != "{OriginalFormat}").ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Enqueue(new FollowUpLogEntry(logLevel, exception, formatter(state, exception), properties));
        }
    }

    private sealed record FollowUpLogEntry(
        LogLevel Level,
        Exception? Exception,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> values = new Dictionary<string, object>();

        public IDictionary<string, object> LoadTempData(HttpContext context) => values;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) =>
            this.values = values;
    }
}
