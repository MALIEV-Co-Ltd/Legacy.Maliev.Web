using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Polly;

namespace Legacy.Maliev.Web.Infrastructure;

/// <summary>Source-ordered fill-only CNC completion; not atomic and not safe to replay.</summary>
internal sealed class CncProfilePersistenceClient(IHttpClientFactory clients, IServiceAccessTokenProvider tokens) : ICncProfilePersistenceClient
{
    private static readonly JsonSerializerOptions WireJson = new() { PropertyNamingPolicy = null };

    public async Task<CncProfilePersistenceResult> CompleteAsync(CncProfilePersistenceRequest request, CancellationToken cancellationToken)
    {
        var progress = new Progress(request?.QuotationRequestId ?? 0);
        if (request is null || cancellationToken.IsCancellationRequested || !Valid(request))
            return progress.Result(CncProfilePersistenceOutcome.Failed);
        try
        {
            var token = await tokens.GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token)) return progress.Result(CncProfilePersistenceOutcome.Failed);
            var customerClient = clients.CreateClient("customers");
            var countryClient = clients.CreateClient("countries");
            var stored = request.PreparedCustomer;
            var completion = request.Completion;
            var customer = new CustomerWrite(Fill(stored.FirstName, completion.FirstName)!, Fill(stored.LastName, completion.LastName)!,
                Fill(stored.Telephone, completion.Telephone), Fill(stored.Mobile, completion.Mobile), stored.Fax,
                Fill(stored.Email, completion.Email)!, stored.DateOfBirth, stored.CompanyId, stored.BillingAddressId, stored.ShippingAddressId);
            var original = new CustomerWrite(stored.FirstName, stored.LastName, stored.Telephone, stored.Mobile, stored.Fax,
                stored.Email, stored.DateOfBirth, stored.CompanyId, stored.BillingAddressId, stored.ShippingAddressId);

            progress.Stage = CncProfilePersistenceStage.Company;
            if (stored.Company is null && (!string.IsNullOrWhiteSpace(completion.Company) || !string.IsNullOrWhiteSpace(completion.FormattedTaxNumber)))
            {
                var company = new CompanyWrite(completion.Company, completion.FormattedTaxNumber, null);
                var id = await WriteAsync(customerClient, HttpMethod.Post, "customers/companies", company,
                    (root, newId) => CompanyMatches(root, company) && MatchingLocationPath(newId, "customers/Companies"), token, progress, cancellationToken);
                progress.CompanyId = id;
                customer = customer with { CompanyId = id };
            }
            else if (stored.Company is { } existingCompany)
            {
                var company = new CompanyWrite(Fill(existingCompany.Name, completion.Company)!, Fill(existingCompany.TaxNumber, completion.FormattedTaxNumber), existingCompany.Registrar);
                if (company != new CompanyWrite(existingCompany.Name, existingCompany.TaxNumber, existingCompany.Registrar))
                    await WriteAsync(customerClient, HttpMethod.Put, $"customers/companies/{existingCompany.Id}", company, null, token, progress, cancellationToken);
            }

            progress.Stage = CncProfilePersistenceStage.BillingCountry;
            var billingCountry = await ResolveCountryAsync(countryClient, completion.Country, token, progress, cancellationToken);
            progress.Stage = CncProfilePersistenceStage.BillingAddress;
            var billing = stored.BillingAddress;
            if (billing is null)
            {
                var payload = NewAddress(completion.Billing, billingCountry);
                var id = await WriteAsync(customerClient, HttpMethod.Post, $"customers/{stored.Id}/addresses", payload,
                    (root, newId) => AddressMatches(root, payload) && MatchingLocationPath(newId, $"customers/{stored.Id}/addresses"), token, progress, cancellationToken);
                progress.BillingId = id;
                customer = customer with { BillingAddressId = id };
                billing = new CustomerAddress(id, payload.Building, payload.AddressLine1, payload.AddressLine2, payload.City, payload.State, payload.PostalCode, payload.CountryId, null, null);
            }
            else
            {
                var payload = FilledAddress(billing, completion.Billing, billingCountry);
                if (payload != ExistingAddress(billing))
                    await WriteAsync(customerClient, HttpMethod.Put, $"customers/addresses/{billing.Id}", payload, null, token, progress, cancellationToken);
            }

            if (completion.ShipToBillingAddress)
            {
                customer = customer with { ShippingAddressId = billing.Id };
            }
            else
            {
                progress.Stage = CncProfilePersistenceStage.ShippingCountry;
                var shippingCountry = await ResolveCountryAsync(countryClient, completion.ShippingCountry, token, progress, cancellationToken);
                progress.Stage = CncProfilePersistenceStage.ShippingAddress;
                if (stored.ShippingAddress is null)
                {
                    var payload = NewAddress(completion.Shipping!, shippingCountry);
                    var id = await WriteAsync(customerClient, HttpMethod.Post, $"customers/{stored.Id}/addresses", payload,
                        (root, newId) => AddressMatches(root, payload) && MatchingLocationPath(newId, $"customers/{stored.Id}/addresses"), token, progress, cancellationToken);
                    progress.ShippingId = id;
                    customer = customer with { ShippingAddressId = id };
                }
                else
                {
                    var payload = FilledAddress(stored.ShippingAddress, completion.Shipping!, shippingCountry);
                    if (payload != ExistingAddress(stored.ShippingAddress))
                        await WriteAsync(customerClient, HttpMethod.Put, $"customers/addresses/{stored.ShippingAddress.Id}", payload, null, token, progress, cancellationToken);
                }
            }

            progress.Stage = CncProfilePersistenceStage.Customer;
            if (customer != original)
                await WriteAsync(customerClient, HttpMethod.Put, $"customers/{stored.Id}", customer, null, token, progress, cancellationToken);
            progress.Stage = CncProfilePersistenceStage.Complete;
            progress.StatusCode = null;
            return progress.Result(CncProfilePersistenceOutcome.Completed);
        }
        catch (Stop exception)
        {
            return progress.Result(exception.Outcome);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException
            or JsonException or InvalidOperationException or ExecutionRejectedException or ArgumentException)
        {
            return progress.Result(progress.WriteStarted ? CncProfilePersistenceOutcome.Unknown : CncProfilePersistenceOutcome.Failed);
        }
    }

    // The callback compares exact producer fields; Location validation is done separately below.
    private async Task<int> WriteAsync(HttpClient client, HttpMethod method, string path, object payload,
        Func<JsonElement, int, bool>? validateCreated, string token, Progress progress, CancellationToken cancellationToken)
    {
        using var message = Authorized(method, path, token);
        message.Content = JsonContent.Create(payload, options: WireJson);
        cancellationToken.ThrowIfCancellationRequested();
        progress.StatusCode = null;
        progress.WriteStarted = true;
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        progress.StatusCode = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.Unauthorized) tokens.Invalidate(token);
        var expected = method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.NoContent;
        if (response.StatusCode != expected)
            throw new Stop(IsDefiniteRejection(response.StatusCode) ? CncProfilePersistenceOutcome.Failed : CncProfilePersistenceOutcome.Unknown);
        var id = 0;
        if (method == HttpMethod.Post)
        {
            using var json = await ReadJsonAsync(response, 65536, cancellationToken);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root)
                || !root.TryGetProperty("Id", out var idElement) || idElement.ValueKind != JsonValueKind.Number
                || !idElement.TryGetInt32(out id) || id <= 0 || !validateCreated!(root, id)
                || !MatchingLocation(response.Headers.Location, message.RequestUri, path, id))
                throw new Stop(CncProfilePersistenceOutcome.Unknown);
        }
        progress.HasConfirmedChanges = true;
        progress.WriteStarted = false;
        return id;
    }

    private async Task<int> ResolveCountryAsync(HttpClient client, string? name, string token, Progress progress, CancellationToken cancellationToken)
    {
        progress.WriteStarted = false;
        progress.StatusCode = null;
        using var message = Authorized(HttpMethod.Get, "countries/", token);
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        progress.StatusCode = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.Unauthorized) tokens.Invalidate(token);
        if (response.StatusCode != HttpStatusCode.OK) throw new Stop(CncProfilePersistenceOutcome.Failed);
        using var json = await ReadJsonAsync(response, 1048576, cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Array) throw new Stop(CncProfilePersistenceOutcome.Failed);
        foreach (var country in json.RootElement.EnumerateArray())
        {
            if (country.ValueKind != JsonValueKind.Object || HasDuplicateProperties(country)
                || !country.TryGetProperty("Name", out var countryName) || countryName.ValueKind != JsonValueKind.String
                || !country.TryGetProperty("Id", out var countryId) || countryId.ValueKind != JsonValueKind.Number
                || !countryId.TryGetInt32(out var id) || id <= 0) throw new Stop(CncProfilePersistenceOutcome.Failed);
            if (string.Equals(countryName.GetString(), name, StringComparison.OrdinalIgnoreCase)) return id;
        }
        throw new Stop(CncProfilePersistenceOutcome.Failed);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, long limit, CancellationToken cancellationToken)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected content type.");
        await response.Content.LoadIntoBufferAsync(limit, cancellationToken);
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    private static bool MatchingLocationPath(int id, string path) => id > 0 && !string.IsNullOrEmpty(path);

    private static bool MatchingLocation(Uri? location, Uri? requestUri, string path, int id)
    {
        if (location is null || requestUri is not { IsAbsoluteUri: true }) return false;
        var expected = "/" + path.Trim('/') + "/" + id;
        if (!location.IsAbsoluteUri) return string.Equals(location.OriginalString, expected, StringComparison.OrdinalIgnoreCase);
        return location.Scheme == requestUri.Scheme && location.Authority == requestUri.Authority
            && string.IsNullOrEmpty(location.UserInfo) && string.IsNullOrEmpty(location.Query) && string.IsNullOrEmpty(location.Fragment)
            && string.Equals(location.AbsolutePath, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasDuplicateProperties(JsonElement root) => root.EnumerateObject().Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count();
    private static bool StringMatches(JsonElement root, string property, string? expected) => root.TryGetProperty(property, out var value)
        ? value.ValueKind == JsonValueKind.Null ? expected is null : value.ValueKind == JsonValueKind.String && value.GetString() == expected
        : expected is null;
    private static bool CompanyMatches(JsonElement root, CompanyWrite expected) => StringMatches(root, "Name", expected.Name.Trim())
        && StringMatches(root, "TaxNumber", expected.TaxNumber) && StringMatches(root, "Registrar", expected.Registrar);
    private static bool AddressMatches(JsonElement root, AddressWrite expected) => StringMatches(root, "Building", expected.Building)
        && StringMatches(root, "AddressLine1", expected.AddressLine1.Trim()) && StringMatches(root, "AddressLine2", expected.AddressLine2)
        && StringMatches(root, "City", expected.City) && StringMatches(root, "State", expected.State) && StringMatches(root, "PostalCode", expected.PostalCode)
        && root.TryGetProperty("CountryId", out var country) && country.ValueKind == JsonValueKind.Number && country.TryGetInt32(out var id) && id == expected.CountryId;
    private static bool IsDefiniteRejection(HttpStatusCode status) => (int)status is 400 or 401 or 403 or 404 or 405 or 413 or 415 or 422;
    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return message;
    }

    private static string? Fill(string? stored, string? completion) => !string.IsNullOrWhiteSpace(stored) || string.IsNullOrWhiteSpace(completion) ? stored : completion.Trim();
    private static AddressWrite ExistingAddress(CustomerAddress address) => new(address.Building, address.AddressLine1, address.AddressLine2, address.City, address.State, address.PostalCode, address.CountryId);
    private static AddressWrite NewAddress(CncProfileAddressFields value, int country) => new(value.Building, value.AddressLine1, value.AddressLine2, value.City, value.State, value.PostalCode, country);
    private static AddressWrite FilledAddress(CustomerAddress stored, CncProfileAddressFields value, int country) => new(Fill(stored.Building, value.Building),
        Fill(stored.AddressLine1, value.AddressLine1)!, Fill(stored.AddressLine2, value.AddressLine2), Fill(stored.City, value.City), Fill(stored.State, value.State),
        Fill(stored.PostalCode, value.PostalCode), stored.CountryId <= 0 && country > 0 ? country : stored.CountryId);
    private static bool Valid(CncProfilePersistenceRequest request) => request.QuotationRequestId > 0 && request.AuthenticatedCustomerId > 0
        && request.PreparedCustomer is { } customer && customer.Id == request.AuthenticatedCustomerId
        && ReferenceMatches(customer.CompanyId, customer.Company?.Id) && ReferenceMatches(customer.BillingAddressId, customer.BillingAddress?.Id)
        && ReferenceMatches(customer.ShippingAddressId, customer.ShippingAddress?.Id)
        && request.Completion is { Company: not null, Billing: not null } completion
        && (completion.ShipToBillingAddress || completion.Shipping is not null);
    private static bool ReferenceMatches(int? expected, int? actual) => expected.HasValue ? expected.Value > 0 && actual == expected : actual is null;

    private sealed record CompanyWrite(string Name, string? TaxNumber, string? Registrar);
    private sealed record AddressWrite(string? Building, string AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode, int CountryId);
    private sealed record CustomerWrite(string FirstName, string LastName, string? Telephone, string? Mobile, string? Fax,
        string Email, DateTime? DateOfBirth, int? CompanyId, int? BillingAddressId, int? ShippingAddressId);
    private sealed class Stop(CncProfilePersistenceOutcome outcome) : Exception
    {
        internal CncProfilePersistenceOutcome Outcome { get; } = outcome;
    }
    private sealed class Progress(int requestId)
    {
        internal CncProfilePersistenceStage Stage = CncProfilePersistenceStage.Preparation;
        internal bool WriteStarted;
        internal bool HasConfirmedChanges;
        internal int? CompanyId;
        internal int? BillingId;
        internal int? ShippingId;
        internal int? StatusCode;
        internal CncProfilePersistenceResult Result(CncProfilePersistenceOutcome outcome) => new(requestId, outcome, Stage, HasConfirmedChanges, CompanyId, BillingId, ShippingId, StatusCode);
    }
}
