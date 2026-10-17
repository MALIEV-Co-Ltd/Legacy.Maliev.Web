using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationSubmissionEndpointTests
{
    [Theory]
    [InlineData(false, "")]
    [InlineData(false, " -- ")]
    [InlineData(false, "—")]
    [InlineData(true, "")]
    [InlineData(true, " -- ")]
    [InlineData(true, "—")]
    public async Task AbsentTaxWithStaleMalformedBranch_ReachesGuestAndPreparedMemberSubmission(
        bool authenticated, string absentTax)
    {
        var service = new RecordingSubmissionService(Completed(751));
        var profiles = new OptionalTaxProfiles(null);
        var preparation = new InstantQuotationAuthenticatedPreparationService(
            new StoredShippingSessions(), new StoredShippingStore(), profiles, new ImmediateCountryClient());
        await using var application = CreateFactory(service, authenticated: authenticated, preparation: preparation);
        using var client = CreateClient(application);
        if (authenticated)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        }
        var token = await GetAntiforgeryTokenAsync(client);
        using var response = await client.PostAsync(SubmitRoute, CustomerForm(new()
        {
            ["__RequestVerificationToken"] = token,
            ["Country"] = "Thailand",
            ["TaxNumber"] = absentTax,
            ["TaxBranch"] = "branch",
            ["TaxBranchCode"] = "not-five-digits",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var call = Assert.Single(service.Calls);
        Assert.Null(call.Customer.TaxIdentification);
        Assert.Equal(authenticated ? "member-42" : null, call.OwnerIdentity);
        if (authenticated)
        {
            var operation = Assert.IsType<InstantQuotationProfileCompletionOperation>(call.Customer.ProfileCompletion);
            Assert.Null(operation.Body.TaxNumber);
            Assert.Null(operation.Body.TaxBranch);
            Assert.Null(operation.Body.TaxBranchCode);
            Assert.Equal("member-42", Assert.Single(profiles.Owners));
        }
        else
        {
            Assert.Null(call.Customer.ProfileCompletion);
            Assert.Empty(profiles.Owners);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PresentTaxWithMalformedBranch_RemainsRejectedBeforePersistence(bool authenticated)
    {
        var service = new RecordingSubmissionService(Completed(752));
        var tempData = new RecordingTempDataProvider();
        var preparation = new InstantQuotationAuthenticatedPreparationService(
            new StoredShippingSessions(), new StoredShippingStore(), new OptionalTaxProfiles(null), new ImmediateCountryClient());
        await using var application = CreateFactory(service, tempData, authenticated, preparation);
        using var client = CreateClient(application);
        if (authenticated)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        }
        var token = await GetAntiforgeryTokenAsync(client);
        tempData.Clear();
        using var response = await client.PostAsync(SubmitRoute, CustomerForm(new()
        {
            ["__RequestVerificationToken"] = token,
            ["Country"] = "Thailand",
            ["TaxNumber"] = "0105559123456",
            ["TaxBranch"] = "branch",
            ["TaxBranchCode"] = "A0007",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(service.Calls);
        var fields = JsonSerializer.Deserialize<string[]>(Assert.IsType<string>(
            tempData.Values[ThreeDimensionalPrinting.ValidationFieldsTempDataKey]));
        Assert.NotNull(fields);
        Assert.Equal(["TaxBranchCode"], fields);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" -- ")]
    [InlineData("—")]
    public async Task BlankPostedTaxCannotClearAuthoritativeProfileTaxOrBranch(string absentTax)
    {
        const string authoritativeTax = "0115562011815 (สาขาที่ 00003)";
        var service = new RecordingSubmissionService(Completed(753));
        var preparation = new InstantQuotationAuthenticatedPreparationService(
            new StoredShippingSessions(), new StoredShippingStore(), new OptionalTaxProfiles(authoritativeTax), new ImmediateCountryClient());
        await using var application = CreateFactory(service, authenticated: true, preparation: preparation);
        using var client = CreateClient(application);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        var token = await GetAntiforgeryTokenAsync(client);
        using var response = await client.PostAsync(SubmitRoute, CustomerForm(new()
        {
            ["__RequestVerificationToken"] = token,
            ["Country"] = "Thailand",
            ["TaxNumber"] = absentTax,
            ["TaxBranch"] = "branch",
            ["TaxBranchCode"] = "forged",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var customer = Assert.Single(service.Calls).Customer;
        Assert.Equal(authoritativeTax, customer.TaxIdentification);
        var operation = Assert.IsType<InstantQuotationProfileCompletionOperation>(customer.ProfileCompletion);
        Assert.Equal("0115562011815", operation.Body.TaxNumber);
        Assert.Equal("branch", operation.Body.TaxBranch);
        Assert.Equal("00003", operation.Body.TaxBranchCode);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, " -- ")]
    [InlineData(false, "—")]
    [InlineData(true, "")]
    [InlineData(true, " -- ")]
    [InlineData(true, "—")]
    public async Task JsonSubmit_AbsentTaxWithStaleBranchUsesExistingTerminalContract(
        bool authenticated, string absentTax)
    {
        var service = new RecordingSubmissionService(Completed(754));
        var preparation = new InstantQuotationAuthenticatedPreparationService(
            new StoredShippingSessions(), new StoredShippingStore(), new OptionalTaxProfiles(null), new ImmediateCountryClient());
        await using var application = CreateFactory(service, authenticated: authenticated, preparation: preparation);
        using var client = CreateClient(application);
        if (authenticated)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        var token = await GetAntiforgeryTokenAsync(client);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.PostAsync(SubmitRoute, CustomerForm(new()
        {
            ["__RequestVerificationToken"] = token,
            ["Country"] = "Thailand",
            ["TaxNumber"] = absentTax,
            ["TaxBranch"] = "branch",
            ["TaxBranchCode"] = "malformed",
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("terminal", result.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("/InstantQuotation/3D-Printing", result.RootElement.GetProperty("redirectUrl").GetString());
        var call = Assert.Single(service.Calls);
        Assert.Null(call.Customer.TaxIdentification);
        Assert.Equal(authenticated ? "member-42" : null, call.OwnerIdentity);
        if (authenticated)
        {
            var operation = Assert.IsType<InstantQuotationProfileCompletionOperation>(call.Customer.ProfileCompletion);
            Assert.Null(operation.Body.TaxNumber);
            Assert.Null(operation.Body.TaxBranch);
            Assert.Null(operation.Body.TaxBranchCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JsonSubmit_PresentTaxMalformedBranchUsesExistingRetryContract(bool authenticated)
    {
        var service = new RecordingSubmissionService(Completed(755));
        var preparation = new InstantQuotationAuthenticatedPreparationService(
            new StoredShippingSessions(), new StoredShippingStore(), new OptionalTaxProfiles(null), new ImmediateCountryClient());
        await using var application = CreateFactory(service, authenticated: authenticated, preparation: preparation);
        using var client = CreateClient(application);
        if (authenticated)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        var token = await GetAntiforgeryTokenAsync(client);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.PostAsync(SubmitRoute, CustomerForm(new()
        {
            ["__RequestVerificationToken"] = token,
            ["Country"] = "Thailand",
            ["TaxNumber"] = "0105559123456",
            ["TaxBranch"] = "branch",
            ["TaxBranchCode"] = "A0007",
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("retry", result.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("TaxBranchCode", Assert.Single(result.RootElement.GetProperty("invalidFields")
            .EnumerateArray().Select(value => value.GetString())));
        Assert.Empty(service.Calls);
    }

    private sealed class OptionalTaxProfiles(string? taxIdentification) : IInstantQuotationProfileCompletionClient
    {
        internal List<string> Owners { get; } = [];
        public Task<InstantQuotationProfileGraphResult> ReadAsync(string ownerIdentity, CancellationToken cancellationToken)
        {
            Owners.Add(ownerIdentity);
            var company = new CustomerCompany(4, "Owner company", taxIdentification, null, null, null);
            var customer = new CustomerAccountDetails(7, "Owner", "Name", "Owner Name", null, null, null,
                "owner@example.test", null, 4, null, null, null, null, null, company, null);
            return Task.FromResult(new InstantQuotationProfileGraphResult(
                new(customer, "\"" + new string('a', 64) + "\""), InstantQuotationProblemCategory.None));
        }
        public Task<InstantQuotationProfileCompletionResult> CompleteAsync(string ownerIdentity,
            InstantQuotationProfileCompletionOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
