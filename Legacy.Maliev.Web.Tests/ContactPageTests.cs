using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ContactPage = Legacy.Maliev.Web.Pages.Contact.Index;

namespace Legacy.Maliev.Web.Tests;

public sealed class ContactPageTests
{
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
        Assert.Equal("request_quote", analyticsEvent.GetProperty("event").GetString());
        Assert.Equal("contact_request", analyticsEvent.GetProperty("intent_type").GetString());
        Assert.Equal("general_contact", analyticsEvent.GetProperty("service").GetString());
        Assert.Equal("message-913", analyticsEvent.GetProperty("transaction_id").GetString());
        Assert.Equal("persisted", analyticsEvent.GetProperty("submission_status").GetString());
        Assert.False(analyticsEvent.GetProperty("has_files").GetBoolean());
        Assert.False(analyticsEvent.GetProperty("file_upload_completed").GetBoolean());
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

    private static ContactPage CreatePage(
        RecordingContactClient contactClient,
        IAntiBotVerifier antiBotVerifier,
        INotificationClient? notificationClient = null,
        IContactTrustedCustomerLoader? trustedCustomerLoader = null)
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
            NullLogger<ContactPage>.Instance)
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

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> values = new Dictionary<string, object>();

        public IDictionary<string, object> LoadTempData(HttpContext context) => values;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) =>
            this.values = values;
    }
}
