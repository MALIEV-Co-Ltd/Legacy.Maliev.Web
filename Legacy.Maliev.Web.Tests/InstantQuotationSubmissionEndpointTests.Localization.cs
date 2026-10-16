using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationSubmissionEndpointTests
{
    [Theory]
    [InlineData(false, "en", "TaxNumber", "Thai tax ID must contain exactly 13 digits.")]
    [InlineData(false, "th", "TaxNumber", "เลขประจำตัวผู้เสียภาษีไทยต้องมีตัวเลข 13 หลัก")]
    [InlineData(true, "en", "TaxNumber", "Thai tax ID must contain exactly 13 digits.")]
    [InlineData(true, "th", "TaxNumber", "เลขประจำตัวผู้เสียภาษีไทยต้องมีตัวเลข 13 หลัก")]
    [InlineData(false, "en", "BillingBuilding", "Building or organization name must be 256 characters or fewer.")]
    [InlineData(false, "th", "BillingBuilding", "ชื่ออาคารหรือองค์กรต้องมีความยาวไม่เกิน 256 ตัวอักษร")]
    [InlineData(true, "en", "BillingBuilding", "Building or organization name must be 256 characters or fewer.")]
    [InlineData(true, "th", "BillingBuilding", "ชื่ออาคารหรือองค์กรต้องมีความยาวไม่เกิน 256 ตัวอักษร")]
    [InlineData(false, "en", "ShippingBuilding", "Building or organization name must be 256 characters or fewer.")]
    [InlineData(false, "th", "ShippingBuilding", "ชื่ออาคารหรือองค์กรต้องมีความยาวไม่เกิน 256 ตัวอักษร")]
    [InlineData(true, "en", "ShippingBuilding", "Building or organization name must be 256 characters or fewer.")]
    [InlineData(true, "th", "ShippingBuilding", "ชื่ออาคารหรือองค์กรต้องมีความยาวไม่เกิน 256 ตัวอักษร")]
    public async Task JsonSubmit_OptionalCustomerValidationUsesSelectedCultureWithoutSubmission(
        bool authenticated, string culture, string field, string expectedMessage)
    {
        var service = new RecordingSubmissionService(Completed(756));
        var preparation = new InstantQuotationAuthenticatedPreparationService(
            new StoredShippingSessions(), new StoredShippingStore(), new ValidationLocalizationProfiles(), new ImmediateCountryClient());
        await using var application = CreateFactory(service, authenticated: authenticated, preparation: preparation);
        using var client = CreateClient(application);
        if (authenticated)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        var token = await GetAntiforgeryTokenAsync(client);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var posted = field == "TaxNumber" ? "invalid-tax-value" : new string('x', 257);
        using var response = await client.PostAsync(SubmitRoute + "&culture=" + culture, CustomerForm(new()
        {
            ["__RequestVerificationToken"] = token,
            ["Country"] = "Thailand",
            ["ShippingStreet1"] = "Shipping Road",
            ["ShippingCity"] = "Bangkok",
            ["ShippingProvince"] = "Bangkok",
            ["ShippingPostalCode"] = "10110",
            ["ShippingCountry"] = "Thailand",
            ["ShipToBillingAddress"] = field == "ShippingBuilding" ? "false" : "true",
            [field] = posted,
        }, includeShipToBilling: field != "ShippingBuilding"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("retry", result.RootElement.GetProperty("outcome").GetString());
        var errors = result.RootElement.GetProperty("errors").EnumerateArray()
            .Select(value => value.GetString()).ToArray();
        Assert.Contains(expectedMessage, errors);
        Assert.Contains(field, result.RootElement.GetProperty("invalidFields").EnumerateArray()
            .Select(value => value.GetString()));
        Assert.Empty(service.Calls);
        Assert.DoesNotContain(posted, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task JsonSubmit_UnknownValidationMessagePreservesFrameworkFallback(string culture)
    {
        var service = new RecordingSubmissionService(Completed(757));
        await using var application = CreateFactory(service);
        using var client = CreateClient(application);
        var token = await GetAntiforgeryTokenAsync(client);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var posted = new string('x', 51);
        using var response = await client.PostAsync(SubmitRoute + "&culture=" + culture, CustomerForm(new()
        {
            ["__RequestVerificationToken"] = token,
            ["FirstName"] = posted,
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("retry", result.RootElement.GetProperty("outcome").GetString());
        Assert.Contains("The field FirstName must be a string with a maximum length of 50.",
            result.RootElement.GetProperty("errors").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("FirstName", result.RootElement.GetProperty("invalidFields")
            .EnumerateArray().Select(value => value.GetString()));
        Assert.Empty(service.Calls);
        Assert.DoesNotContain(posted, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonSubmit_AlreadyLocalizedPreparationErrorRemainsUnchanged()
    {
        var service = new RecordingSubmissionService(Completed(758));
        var preparation = new InstantQuotationAuthenticatedPreparationService(
            new StoredShippingSessions(), new StoredShippingStore(), new UnavailableLocalizationProfiles(), new ImmediateCountryClient());
        await using var application = CreateFactory(service, authenticated: true, preparation: preparation);
        using var client = CreateClient(application);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        var token = await GetAntiforgeryTokenAsync(client);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.PostAsync(SubmitRoute + "&culture=th", CustomerForm(new()
        {
            ["__RequestVerificationToken"] = token,
            ["TaxNumber"] = "0105559123456",
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("retry", result.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("ไม่สามารถโหลดข้อมูลลูกค้าของคุณได้อย่างปลอดภัย กรุณาลองอีกครั้ง",
            Assert.Single(result.RootElement.GetProperty("errors").EnumerateArray().Select(value => value.GetString())));
        Assert.Empty(service.Calls);
    }

    private sealed class UnavailableLocalizationProfiles : IInstantQuotationProfileCompletionClient
    {
        public Task<InstantQuotationProfileGraphResult> ReadAsync(string ownerIdentity, CancellationToken cancellationToken) =>
            Task.FromResult(new InstantQuotationProfileGraphResult(null, InstantQuotationProblemCategory.DependencyUnavailable));

        public Task<InstantQuotationProfileCompletionResult> CompleteAsync(string ownerIdentity,
            InstantQuotationProfileCompletionOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ValidationLocalizationProfiles : IInstantQuotationProfileCompletionClient
    {
        public Task<InstantQuotationProfileGraphResult> ReadAsync(string ownerIdentity, CancellationToken cancellationToken)
        {
            var company = new CustomerCompany(4, "Owner company", null, null, null, null);
            var customer = new CustomerAccountDetails(7, "Owner", "Name", "Owner Name", null, null, null,
                "owner@example.test", null, 4, null, null, null, null, null, company, null);
            return Task.FromResult(new InstantQuotationProfileGraphResult(
                new(customer, "\"" + new string('a', 64) + "\""), InstantQuotationProblemCategory.None));
        }

        public Task<InstantQuotationProfileCompletionResult> CompleteAsync(string ownerIdentity,
            InstantQuotationProfileCompletionOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
