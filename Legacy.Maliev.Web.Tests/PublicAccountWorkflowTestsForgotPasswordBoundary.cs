using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Account;
using Legacy.Maliev.Web.Pages.Account;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Controlled Web boundaries only; no Auth or notification provider qualification.</summary>
public sealed class PublicAccountWorkflowTestsForgotPasswordBoundary(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const string Email = "forgot-boundary@example.invalid";
    private const string Token = "private-reset-token-boundary";
    private const string PasswordProbe = "private-password-boundary";
    private const string ProviderProbe = "private-provider-detail-boundary";

    public static IEnumerable<object[]> ChallengeCases
    {
        get
        {
            foreach (var culture in new[] { "en", "th" })
            {
                for (var flags = 0; flags < 8; flags++)
                {
                    yield return new object[] { culture, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, Token };
                }

                foreach (var unusable in new string?[] { null, "", " \t " })
                {
                    yield return new object[] { culture, true, true, true, unusable! };
                }
            }
        }
    }

    public static IEnumerable<object[]> DeliveryCases =>
        from culture in new[] { "en", "th" }
        from sent in new[] { false, true }
        from available in new[] { false, true }
        from authorized in new[] { false, true }
        select new object[] { culture, sent, available, authorized };

    [Theory]
    [MemberData(nameof(ChallengeCases))]
    public async Task ForgotPassword_ActualFormDispatchRequiresAllChallengeFlagsAndUsableToken(
        string culture, bool accepted, bool available, bool authorized, string? token)
    {
        var account = new AccountBoundary(new CustomerActionChallenge(accepted, token, available, authorized));
        var notification = new NotificationBoundary(_ => Task.FromResult(new NotificationResult(true, true, true)));
        var logs = new CapturedLogger();
        await using var application = Configure(account, notification, logs);
        using var client = Client(application);

        var response = await PostActualForm(client, culture);

        AssertNeutral(response, culture);
        Assert.Equal(1, account.Calls);
        Assert.Equal(Email, account.LastEmail);
        var shouldSend = accepted && available && authorized && !string.IsNullOrWhiteSpace(token);
        Assert.Equal(shouldSend ? 1 : 0, notification.Calls);
        AssertPrivateDataAbsent(response.Body, logs);
        if (shouldSend)
        {
            var message = Assert.IsType<EmailNotification>(notification.Last);
            Assert.Equal(NotificationChannel.NoReply, notification.Channel);
            Assert.Equal(Email, message.To);
            Assert.Equal(culture == "th" ? "รีเซ็ตรหัสผ่าน MALIEV ของคุณ" : "Reset your MALIEV password", message.Subject);
            var link = Match(message.Body, "href=\"(?<value>[^\"]+)\"");
            var callback = new Uri(WebUtility.HtmlDecode(link.Groups["value"].Value));
            Assert.Equal("https", callback.Scheme);
            Assert.Equal("www.maliev.com", callback.Host);
            Assert.Equal("/Account/ResetPassword", callback.AbsolutePath);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(callback.Query);
            Assert.Equal(Email, query["email"].ToString());
            Assert.Equal(token, query["token"].ToString());
            Assert.Equal(culture, query["culture"].ToString());
        }
        else
        {
            Assert.Null(notification.Last);
            Assert.Empty(logs.Entries);
        }
    }

    [Theory]
    [MemberData(nameof(DeliveryCases))]
    public async Task ForgotPassword_DeliveryClassificationIsObservedWithoutChangingNeutralResponse(
        string culture, bool sent, bool available, bool authorized)
    {
        var account = new AccountBoundary(new CustomerActionChallenge(true, Token, true, true));
        var notification = new NotificationBoundary(_ => Task.FromResult(new NotificationResult(sent, available, authorized)));
        var logs = new CapturedLogger();
        await using var application = Configure(account, notification, logs);
        using var client = Client(application);

        var response = await PostActualForm(client, culture);

        AssertNeutral(response, culture);
        Assert.Equal(1, notification.Calls);
        var entry = Assert.Single(logs.Entries);
        Assert.Null(entry.Exception);
        Assert.Equal(sent, Assert.IsType<bool>(entry.Fields["Sent"]));
        Assert.Equal(available, Assert.IsType<bool>(entry.Fields["ServiceAvailable"]));
        Assert.Equal(authorized, Assert.IsType<bool>(entry.Fields["Authorized"]));
        Assert.Equal(sent && available && authorized ? LogLevel.Information : LogLevel.Warning, entry.Level);
        AssertPrivateDataAbsent(response.Body, logs);
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("th", false)]
    [InlineData("en", true)]
    [InlineData("th", true)]
    public async Task ForgotPassword_NonRequestDeliveryFaultRemainsNeutralAndDoesNotLogProviderDetails(
        string culture, bool providerCancellation)
    {
        var account = new AccountBoundary(new CustomerActionChallenge(true, Token, true, true));
        var details = string.Join("|", Email, Token, PasswordProbe, ProviderProbe);
        Exception failure = providerCancellation
            ? new OperationCanceledException(details)
            : new InvalidOperationException(details);
        var notification = new NotificationBoundary(_ => Task.FromException<NotificationResult>(failure));
        var logs = new CapturedLogger();
        await using var application = Configure(account, notification, logs);
        using var client = Client(application);

        var response = await PostActualForm(client, culture);

        AssertNeutral(response, culture);
        Assert.Equal(1, notification.Calls);
        var entry = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        AssertPrivateDataAbsent(response.Body, logs);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ForgotPassword_ForgedAntiforgeryNeverReachesEitherBoundary(string culture)
    {
        var account = new AccountBoundary(new CustomerActionChallenge(true, Token, true, true));
        var notification = new NotificationBoundary(_ => Task.FromResult(new NotificationResult(true, true, true)));
        var logs = new CapturedLogger();
        await using var application = Configure(account, notification, logs);
        using var client = Client(application);

        var response = await PostActualForm(client, culture, forgeToken: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(0, account.Calls);
        Assert.Equal(0, notification.Calls);
        Assert.Empty(logs.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForgotPassword_RequestCancellationPropagatesWithoutDeliveryClassification(bool duringDelivery)
    {
        using var cancellation = new CancellationTokenSource();
        var account = new AccountBoundary(new CustomerActionChallenge(true, Token, true, true));
        var notification = new NotificationBoundary(token =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<NotificationResult>(token);
        });
        if (!duringDelivery)
        {
            cancellation.Cancel();
            account.OnRequest = token => Task.FromCanceled<CustomerActionChallenge>(token);
        }

        var logs = new CapturedLogger();
        var page = new ForgotPassword(account, notification, new EchoLocalizer(), logs)
        {
            Email = Email,
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext(),
                RouteData = new RouteData(),
                ActionDescriptor = new CompiledPageActionDescriptor(),
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => page.OnPostPasswordResetAsync(cancellation.Token));

        Assert.Equal(duringDelivery ? 1 : 0, notification.Calls);
        Assert.Empty(logs.Entries);
    }

    private WebApplicationFactory<Program> Configure(
        AccountBoundary account, NotificationBoundary notification, CapturedLogger logs) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICustomerAuthenticationClient>();
            services.RemoveAll<INotificationClient>();
            services.AddSingleton<ICustomerAuthenticationClient>(account);
            services.AddSingleton<INotificationClient>(notification);
            services.AddSingleton<ILogger<ForgotPassword>>(logs);
        }));

    private static HttpClient Client(WebApplicationFactory<Program> application) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://www.maliev.com"),
            AllowAutoRedirect = false,
        });

    private static async Task<PostObservation> PostActualForm(HttpClient client, string culture, bool forgeToken = false)
    {
        using var page = await client.GetAsync("/account/forgotpassword?culture=" + culture);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        var form = Assert.Single(Regex.Matches(html, "<form[^>]*>[\\s\\S]*?</form>",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).Cast<Match>()
            .Where(candidate => candidate.Value.Contains("handler=PasswordReset", StringComparison.Ordinal)));
        var action = Match(form.Value, "formaction=\"(?<value>[^\"]+)\"").Groups["value"].Value;
        var anti = Match(form.Value, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"").Groups["value"].Value;
        using var response = await client.PostAsync(action, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = Email,
            ["__RequestVerificationToken"] = forgeToken ? "synthetic-forged-antiforgery" : anti,
        }));
        var location = response.Headers.Location;
        if (response.StatusCode == HttpStatusCode.Redirect && location is not null)
        {
            using var redirected = await client.GetAsync(location);
            Assert.Equal(HttpStatusCode.OK, redirected.StatusCode);
            return new(response.StatusCode, location, await redirected.Content.ReadAsStringAsync());
        }

        return new(response.StatusCode, location, await response.Content.ReadAsStringAsync());
    }

    private static Match Match(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        Assert.True(match.Success);
        return match;
    }

    private static void AssertNeutral(PostObservation response, string culture)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.Status);
        var location = Assert.IsType<Uri>(response.Location);
        Assert.DoesNotContain(Email, location.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, location.ToString(), StringComparison.Ordinal);
        Assert.Contains("culture=" + culture, location.ToString(), StringComparison.Ordinal);
        Assert.Contains(culture == "th"
            ? "หากพบบัญชีที่เข้าเงื่อนไข ระบบได้ส่งลิงก์รีเซ็ตรหัสผ่านแล้ว"
            : "If an eligible account exists, a password reset link has been sent.",
            WebUtility.HtmlDecode(response.Body), StringComparison.Ordinal);
    }

    private static void AssertPrivateDataAbsent(string html, CapturedLogger logs)
    {
        foreach (var secret in new[] { Email, Token, PasswordProbe, ProviderProbe })
        {
            Assert.DoesNotContain(secret, html, StringComparison.Ordinal);
            foreach (var entry in logs.Entries)
            {
                Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
                Assert.Null(entry.Exception);
                foreach (var field in entry.Fields.Values)
                {
                    Assert.DoesNotContain(secret, field?.ToString() ?? "", StringComparison.Ordinal);
                }
            }
        }
    }

    private sealed record PostObservation(HttpStatusCode Status, Uri? Location, string Body);
    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Fields);

    private sealed class CapturedLogger : ILogger<ForgotPassword>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Enqueue(new(logLevel, formatter(state, exception), exception, fields));
        }
    }

    private sealed class NotificationBoundary(Func<CancellationToken, Task<NotificationResult>> send) : INotificationClient
    {
        public int Calls { get; private set; }
        public EmailNotification? Last { get; private set; }
        public NotificationChannel Channel { get; private set; }
        public Task<NotificationResult> SendAsync(NotificationChannel channel, EmailNotification notification, CancellationToken cancellationToken)
        {
            Calls++;
            Last = notification;
            Channel = channel;
            return send(cancellationToken);
        }
    }

    private sealed class AccountBoundary(CustomerActionChallenge challenge) : ICustomerAuthenticationClient
    {
        public int Calls { get; private set; }
        public string? LastEmail { get; private set; }
        public Func<CancellationToken, Task<CustomerActionChallenge>>? OnRequest { get; set; }
        public Task<CustomerActionChallenge> RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
        {
            Calls++;
            LastEmail = email;
            return OnRequest?.Invoke(cancellationToken) ?? Task.FromResult(challenge);
        }

        public Task<CustomerSelfIdentityResult> GetSelfIdentityAsync(string accessToken, int expectedCustomerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAuthenticationResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerIdentityRegistration> RegisterAsync(int databaseId, string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompleteEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerEmailChangeValidationResult> ValidateEmailChangeAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerEmailChangeCompletionResult> CompleteEmailChangeAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompletePasswordResetAsync(string email, string token, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerCredentialOperationResult> ChangeEmailAsync(string accessToken, string currentPassword, string newEmail, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerCredentialOperationResult> ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EchoLocalizer : IStringLocalizer<ForgotPasswordContent>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
