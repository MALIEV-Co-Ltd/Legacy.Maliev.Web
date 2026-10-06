using System.Net;
using System.Net.Http.Headers;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationSubmissionEndpointTests
{
    [Fact]
    public async Task AuthenticatedStoredShippingWithoutBilling_PostedTrueReachesSubmissionAsDistinctStoredShipping()
    {
        var profiles = new StoredShippingProfiles();
        var preparation = new InstantQuotationAuthenticatedPreparationService(
            new StoredShippingSessions(), new StoredShippingStore(), profiles, new ImmediateCountryClient());
        var submission = new RecordingSubmissionService(Completed(742));
        await using var application = CreateFactory(submission, authenticated: true, preparation: preparation);
        using var client = CreateClient(application);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await client.PostAsync(SubmitRoute, CustomerForm(new()
        {
            ["__RequestVerificationToken"] = token,
            ["Country"] = "Thailand",
            ["ShipToBillingAddress"] = "true",
            ["ShippingBuilding"] = "Forged warehouse",
            ["ShippingStreet1"] = "Forged shipping",
            ["ShippingCity"] = "Forged city",
            ["ShippingCountry"] = "Forged country",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("member-42", Assert.Single(profiles.Owners));
        var call = Assert.Single(submission.Calls);
        Assert.Equal("member-42", call.OwnerIdentity);
        Assert.False(call.Customer.ShipToBillingAddress);
        Assert.Equal("Stored shipping", call.Customer.ShippingAddressLine1);
        Assert.Equal("Stored warehouse", call.Customer.ShippingBuilding);
        Assert.Equal("Bang Na", call.Customer.ShippingCity);
        Assert.Equal("Thailand", call.Customer.ShippingCountry);
        Assert.Equal("Billing Road", call.Customer.BillingAddressLine1);
        var operation = Assert.IsType<InstantQuotationProfileCompletionOperation>(call.Customer.ProfileCompletion);
        Assert.Equal(7, operation.CustomerId);
        Assert.False(operation.Body.ShipToBillingAddress);
        Assert.Equal("Stored shipping", operation.Body.Shipping?.AddressLine1);
        Assert.Equal(1, operation.Body.Shipping?.CountryId);
        Assert.Equal("Billing Road", operation.Body.Billing?.AddressLine1);
    }

    private sealed class StoredShippingProfiles : IInstantQuotationProfileCompletionClient
    {
        internal List<string> Owners { get; } = [];
        public Task<InstantQuotationProfileGraphResult> ReadAsync(string ownerIdentity, CancellationToken cancellationToken)
        {
            Owners.Add(ownerIdentity);
            var shipping = new CustomerAddress(12, "Stored warehouse", "Stored shipping", null,
                "Bang Na", "Bangkok", "10260", 1, null, null);
            var customer = new CustomerAccountDetails(7, "Owner", "Name", "Owner Name", null, null, null,
                "owner@example.test", null, null, null, 12, null, null, null, null, shipping);
            return Task.FromResult(new InstantQuotationProfileGraphResult(
                new(customer, "\"" + new string('a', 64) + "\""), InstantQuotationProblemCategory.None));
        }
        public Task<InstantQuotationProfileCompletionResult> CompleteAsync(string ownerIdentity,
            InstantQuotationProfileCompletionOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StoredShippingSessions : IInstantQuotationSessionStore
    {
        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            Task.FromResult<InstantQuotationSessionState?>(new(sessionId, new string('a', 64), new([]), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState state, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StoredShippingStore : IInstantQuotationSubmissionStore
    {
        public Task<IInstantQuotationSubmissionLease?> TryAcquireAsync(string submissionId, string ownerIdentity, CancellationToken cancellationToken) =>
            Task.FromResult<IInstantQuotationSubmissionLease?>(new StoredShippingLease());
    }

    private sealed class StoredShippingLease : IInstantQuotationSubmissionLease
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<bool> RenewAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<InstantQuotationSubmissionCheckpointRead> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InstantQuotationSubmissionCheckpointRead(true, null));
        public Task<bool> TryPutAsync(InstantQuotationSubmissionCheckpoint value,
            InstantQuotationSubmissionCheckpointStatus? expectedPriorStatus, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
