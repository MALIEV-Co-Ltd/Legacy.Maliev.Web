using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Infrastructure;

public sealed class InstantQuotationProfileCompletionOptions
{
    public const string SectionName = "InstantQuotation:AuthenticatedProfileCompletion";
    public bool Enabled { get; set; }
}

internal sealed class InstantQuotationProfileCompletionClient(
    IHttpClientFactory clientFactory, IAccountSessionManager sessions, IHttpContextAccessor contextAccessor,
    IOptions<InstantQuotationProfileCompletionOptions> options, ICustomerAuthenticationClient authentication) : IInstantQuotationProfileCompletionClient
{
    internal const string CapabilityHeader = "X-Quotation-Profile-Completion-Contract";
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<InstantQuotationProfileGraphResult> ReadAsync(string ownerIdentity, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return new(null, InstantQuotationProblemCategory.DependencyUnavailable);
        try
        {
            var owner = await ResolveOwnerAsync(ownerIdentity, cancellationToken);
            if (owner is null) return new(null, InstantQuotationProblemCategory.Authorization);
            using var request = Request(HttpMethod.Get, owner.Id, owner.Token);
            using var response = await clientFactory.CreateClient("customers").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return new(null, Category(response.StatusCode));
            if (!response.Headers.TryGetValues(CapabilityHeader, out var values)
                || values.ToArray() is not ["2"]
                || !IsEntityTag(response.Headers.ETag?.ToString()))
                return new(null, InstantQuotationProblemCategory.DependencyUnavailable);

            var customer = await response.Content.ReadFromJsonAsync<CustomerAccountDetails>(WireOptions, cancellationToken);
            if (customer is null || customer.Id != owner.Id) return new(null, InstantQuotationProblemCategory.Authorization);
            string? email = null;
            string? mobile = null;
            if (string.IsNullOrWhiteSpace(customer.Email) || string.IsNullOrWhiteSpace(customer.Mobile))
            {
                var identity = await authentication.GetSelfIdentityAsync(owner.Token, owner.Id, cancellationToken);
                if (identity.Status != CustomerSelfIdentityStatus.Succeeded || identity.Identity?.CustomerId != owner.Id)
                    return new(null, identity.Status is CustomerSelfIdentityStatus.NotAuthorized or CustomerSelfIdentityStatus.IdentityMismatch
                        ? InstantQuotationProblemCategory.Authorization : InstantQuotationProblemCategory.DependencyUnavailable);
                email = string.IsNullOrWhiteSpace(customer.Email) ? identity.Identity.Email : null;
                mobile = identity.Identity.Mobile;
                if (string.IsNullOrWhiteSpace(customer.Email)
                    && (email is null || !IsEmail(email) || !HasMatchingValidatedTokenEmail(owner.Token, email)))
                    return new(null, InstantQuotationProblemCategory.Authorization);
                if (mobile is not null && (string.IsNullOrWhiteSpace(mobile) || mobile != mobile.Trim() || mobile.Length > 50))
                    return new(null, InstantQuotationProblemCategory.DependencyUnavailable);
            }
            return new(new(customer, response.Headers.ETag!.ToString(), email, mobile), InstantQuotationProblemCategory.None);
        }
        catch (Exception exception) when (IsBoundaryFailure(exception, cancellationToken))
        {
            return new(null, InstantQuotationProblemCategory.DependencyUnavailable);
        }
    }

    public async Task<InstantQuotationProfileCompletionResult> CompleteAsync(string ownerIdentity,
        InstantQuotationProfileCompletionOperation operation, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return new(false, InstantQuotationProblemCategory.DependencyUnavailable);
        try
        {
            var owner = await ResolveOwnerAsync(ownerIdentity, cancellationToken);
            if (owner is null || owner.Id != operation.CustomerId) return new(false, InstantQuotationProblemCategory.Authorization);
            if (operation.Key == Guid.Empty || !IsEntityTag(operation.EntityTag)) return new(false, InstantQuotationProblemCategory.Validation);
            using var request = Request(HttpMethod.Post, owner.Id, owner.Token);
            request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(operation.EntityTag));
            request.Headers.Add("Idempotency-Key", operation.Key.ToString("D"));
            request.Content = JsonContent.Create(operation.Body, options: WireOptions);
            using var response = await clientFactory.CreateClient("customers").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return new(false, Category(response.StatusCode));
            var receipt = await response.Content.ReadFromJsonAsync<Receipt>(WireOptions, cancellationToken);
            return receipt?.CustomerId == owner.Id && receipt.CompletionId != Guid.Empty
                ? new(true, InstantQuotationProblemCategory.None) : new(false, InstantQuotationProblemCategory.Unexpected);
        }
        catch (Exception exception) when (IsBoundaryFailure(exception, cancellationToken))
        {
            return new(false, InstantQuotationProblemCategory.DependencyUnavailable);
        }
    }

    private async Task<Owner?> ResolveOwnerAsync(string ownerIdentity, CancellationToken cancellationToken)
    {
        var context = contextAccessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true) return null;
        string? Unique(string type) => context.User.FindAll(type).Select(item => item.Value).ToArray() is [var value] ? value : null;
        if (!string.Equals(Unique("identity_kind"), "customer", StringComparison.Ordinal)
            || !string.Equals(Unique(ClaimTypes.NameIdentifier), ownerIdentity, StringComparison.Ordinal)
            || !int.TryParse(Unique("legacy_database_id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id <= 0 || !string.Equals(ownerIdentity, $"customer:{id}", StringComparison.Ordinal)) return null;
        var sessionId = await sessions.GetCustomerDatabaseIdAsync(context, cancellationToken);
        if (sessionId != id) return null;
        var token = await sessions.GetAccessTokenAsync(context, cancellationToken);
        return string.IsNullOrWhiteSpace(token) ? null : new(id, token);
    }

    internal static bool IsEntityTag(string? value) => value is { Length: 66 }
        && value[0] == '"' && value[^1] == '"' && value[1..^1].All(Uri.IsHexDigit);

    private static bool IsEmail(string value) => value.Length <= 50 && value == value.Trim()
        && MailAddress.TryCreate(value, out var address) && address.DisplayName.Length == 0 && address.Address == value;

    // This is consistency metadata only, AFTER CustomerService authenticated this exact owner.
    // Never use these parsed values to validate a signature, identify an owner or authorize a request.
    private static bool HasMatchingValidatedTokenEmail(string token, string currentEmail)
    {
        try
        {
            if (token.Length > 32768 || token.Split('.') is not [_, var payload, _]) return false;
            using var json = JsonDocument.Parse(WebEncoders.Base64UrlDecode(payload));
            if (json.RootElement.ValueKind != JsonValueKind.Object) return false;
            var fields = json.RootElement.EnumerateObject().Where(field => field.NameEquals("email")).ToArray();
            return fields is [var field] && field.Value.ValueKind == JsonValueKind.String
                && field.Value.GetString() is { } email && IsEmail(email)
                && string.Equals(email, currentEmail, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, int id, string token)
    {
        var request = new HttpRequestMessage(method, $"customers/{id}/instant-quotation-profile-completion");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
    private static InstantQuotationProblemCategory Category(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => InstantQuotationProblemCategory.Authorization,
        HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed => InstantQuotationProblemCategory.Conflict,
        HttpStatusCode.BadRequest => InstantQuotationProblemCategory.Validation,
        _ => InstantQuotationProblemCategory.DependencyUnavailable,
    };
    private static bool IsBoundaryFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException or TimeoutException or Polly.Timeout.TimeoutRejectedException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
    private sealed record Owner(int Id, string Token);
    private sealed record Receipt(int CustomerId, Guid CompletionId, bool Changed);
}
