using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.DependencyInjection;
using Polly.Timeout;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class ThaiLookupClient(IHttpClientFactory clients, IServiceAccessTokenProvider tokens) : IThaiLookupClient
{
    internal static readonly HttpRequestOptionsKey<bool> InteractiveRetryKey = new("maliev-thai-lookup-interactive");

    public Task<LookupResult<ThaiAddressPage>> SearchAddressAsync(ThaiAddressQuery query, CancellationToken cancellationToken)
    {
        if (!ThaiLookupValidation.Valid(query)) return Task.FromResult(new LookupResult<ThaiAddressPage>(null, 400));
        var values = new Dictionary<string, string?>
        {
            ["q"] = query.Q,
            ["limit"] = query.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["cursor"] = query.Cursor,
            ["provinceCode"] = query.Constraints?.ProvinceCode,
            ["districtCode"] = query.Constraints?.DistrictCode,
            ["subdistrictCode"] = query.Constraints?.SubdistrictCode,
            ["postcode"] = query.Constraints?.Postcode is { } postcode ? ThaiLookupValidation.NormalizeDigits(postcode) : null,
        };
        return SendAsync<ThaiAddressPage>(new HttpRequestMessage(HttpMethod.Get,
            "api/v1/thai-addresses/autocomplete?" + Query(values)), cancellationToken);
    }

    public Task<LookupResult<ThaiAddressResolution>> ResolveAddressAsync(ThaiAddressResolveRequest request, CancellationToken cancellationToken)
    {
        if (!ThaiLookupValidation.Valid(request)) return Task.FromResult(new LookupResult<ThaiAddressResolution>(null, 400));
        var normalized = request with { Constraints = Normalize(request.Constraints) };
        return SendAsync<ThaiAddressResolution>(new HttpRequestMessage(HttpMethod.Post, "api/v1/thai-addresses/resolve")
        { Content = JsonContent.Create(normalized) }, cancellationToken);
    }

    public Task<LookupResult<CompanyLookupPage>> SearchCompanyAsync(CompanyLookupQuery query, CancellationToken cancellationToken)
    {
        if (!ThaiLookupValidation.Valid(query)) return Task.FromResult(new LookupResult<CompanyLookupPage>(null, 400));
        return SendAsync<CompanyLookupPage>(new HttpRequestMessage(HttpMethod.Get, "api/v1/companies/search?" + Query(new()
        {
            ["q"] = query.QueryType == "tax-id" ? ThaiLookupValidation.NormalizeDigits(query.Q) : query.Q,
            ["queryType"] = query.QueryType,
            ["language"] = query.Language,
            ["limit"] = query.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        })), cancellationToken);
    }

    private static ThaiAddressConstraints? Normalize(ThaiAddressConstraints? value) => value is null ? null
        : value with { Postcode = value.Postcode is null ? null : ThaiLookupValidation.NormalizeDigits(value.Postcode) };
    private static string Query(Dictionary<string, string?> values) => string.Join("&", values.Where(pair => pair.Value is not null)
        .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value!)));

    private async Task<LookupResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            request.Options.Set(InteractiveRetryKey, true);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            var operationToken = deadline.Token;
            try
            {
                var token = await tokens.GetAccessTokenAsync(operationToken);
                if (string.IsNullOrWhiteSpace(token)) return new(default, 503);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await clients.CreateClient("catalog").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, operationToken);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    tokens.Invalidate(token);
                    return new(default, 503);
                }
                if (!response.IsSuccessStatusCode) return new(default, response.StatusCode is HttpStatusCode.BadRequest
                    or HttpStatusCode.UnprocessableEntity or HttpStatusCode.TooManyRequests ? (int)response.StatusCode : 503);
                // Bound even chunked content before deserialization; never log the address or company query.
                await using var stream = await response.Content.ReadAsStreamAsync(operationToken);
                using var buffer = new MemoryStream();
                var bytes = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(bytes, operationToken)) > 0)
                {
                    if (buffer.Length + count > 262144) return new(default, 503);
                    await buffer.WriteAsync(bytes.AsMemory(0, count), operationToken);
                }
                buffer.Position = 0;
                var value = await JsonSerializer.DeserializeAsync<T>(buffer, new JsonSerializerOptions(JsonSerializerDefaults.Web), operationToken);
                return ValidReply(value) ? new(value, 200) : new(default, 503);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or TimeoutRejectedException
                || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return new(default, 503);
            }
        }
    }

    private static bool ValidReply<T>(T? value) => value switch
    {
        ThaiAddressPage page => !string.IsNullOrWhiteSpace(page.DatasetVersion) && page.Items is not null
            && page.Items.Count <= 50 && page.Items.All(ValidCombination)
            && (!page.HasMore || !string.IsNullOrWhiteSpace(page.NextCursor)),
        ThaiAddressResolution result => !string.IsNullOrWhiteSpace(result.DatasetVersion)
            && result.Outcome is "exact" or "ambiguous" or "not-found" or "conflict"
            && result.OriginalText is not null && result.NormalizedText is not null && result.DetailText is not null
            && result.Candidates is not null && result.Candidates.Count <= 50 && result.Candidates.All(ValidCombination)
            && result.UniqueFields is not null && result.ExtractedSpans is not null && result.Conflicts is not null,
        CompanyLookupPage page => page.Outcome is "matches" or "no-match" or "unavailable" or "unsupported"
            && page.Capability is "suggestion" or "detail" && !string.IsNullOrWhiteSpace(page.Provider)
            && page.Items is not null && page.Items.Count <= 50 && page.Items.All(item => item is not null),
        _ => false,
    };

    private static bool ValidCombination(ThaiAddressCombination value) => value is not null
        && value.Province is not null && value.District is not null && value.Subdistrict is not null
        && !string.IsNullOrWhiteSpace(value.Province.Code) && !string.IsNullOrWhiteSpace(value.District.Code)
        && !string.IsNullOrWhiteSpace(value.Subdistrict.Code)
        && !string.IsNullOrWhiteSpace(value.Province.NameTh) && !string.IsNullOrWhiteSpace(value.District.NameTh)
        && !string.IsNullOrWhiteSpace(value.Subdistrict.NameTh)
        && value.District.ProvinceCode == value.Province.Code && value.Subdistrict.DistrictCode == value.District.Code
        && ThaiLookupValidation.Valid(new ThaiAddressConstraints(value.Province.Code, value.District.Code, value.Subdistrict.Code, value.Postcode))
        && value.Postcode is not null;
}

public static class ThaiLookupServiceRegistration
{
    // Call after the existing infrastructure registration; reuses its catalog client and workload token provider.
    public static IServiceCollection AddThaiLookupClient(this IServiceCollection services) => services.AddScoped<IThaiLookupClient, ThaiLookupClient>();
}
