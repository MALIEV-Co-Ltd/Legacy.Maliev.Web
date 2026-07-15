using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
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
        var page = CreatePage(contactClient, new StubAntiBotVerifier(true));

        var result = await page.OnPostSubmitRequestAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Index", redirect.PageName);
        Assert.Equal(1, contactClient.CallCount);
        var analyticsPayload = Assert.Single(page.TempData.Values.OfType<string>(), value => value.Contains("913", StringComparison.Ordinal));
        Assert.DoesNotContain("mali@example.com", analyticsPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Please contact me", analyticsPayload, StringComparison.OrdinalIgnoreCase);
    }

    private static ContactPage CreatePage(
        RecordingContactClient contactClient,
        IAntiBotVerifier antiBotVerifier)
    {
        var httpContext = new DefaultHttpContext();
        var page = new ContactPage(
            new StubCountryClient(),
            contactClient,
            antiBotVerifier,
            Options.Create(
                new RecaptchaEnterpriseOptions
                {
                    SiteKey = "test-site-key",
                    ProjectId = "test-project"
                }),
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

        public Task<ContactSubmissionResult> SubmitAsync(
            ContactSubmission submission,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result ?? new ContactSubmissionResult(null, true, true));
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
            Assert.Equal("browser-token", token);
            return Task.FromResult(valid);
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
