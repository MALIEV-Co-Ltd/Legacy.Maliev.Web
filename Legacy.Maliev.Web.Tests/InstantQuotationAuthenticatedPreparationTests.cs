using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationAuthenticatedPreparationTests
{
    [Fact]
    public async Task FreshPost_RereadsOwnerGraphAndMergesBeforeBuildingTypedOperation()
    {
        var profiles = new Profiles();
        var service = new InstantQuotationAuthenticatedPreparationService(new Sessions(), new Store(), profiles, new Countries());
        var result = await service.PrepareAsync("session", "customer:7", Posted() with
        {
            FirstName = "forged",
            Email = "forged@example.test",
            ShipToBillingAddress = true,
            TaxNumber = "0115562011815",
            TaxBranch = "branch",
            TaxBranchCode = "3",
        }, "project", default);
        Assert.Equal("Owner", result.Customer?.FirstName);
        Assert.Equal("owner@example.test", result.Customer?.Email);
        Assert.False(result.Customer?.ShipToBillingAddress);
        Assert.Equal("Stored shipping", result.Customer?.ShippingAddressLine1);
        Assert.Equal("00003", result.Customer?.ProfileCompletion?.Body.TaxBranchCode);
        Assert.Equal("0115562011815 (สาขาที่ 00003)", result.Customer?.TaxIdentification);
        Assert.Equal(1, profiles.Reads);
        Assert.Null(result.TerminalResult);
    }

    [Fact]
    public async Task DurableRetry_DoesNotRereadGraphOrChangeFrozenBodyKeyReference()
    {
        var profiles = new Profiles();
        var operation = new InstantQuotationProfileCompletionOperation(7, Guid.NewGuid(), "\"" + new string('a', 64) + "\"",
            new("Owner", "Name", null, "080", null, null, null, null, false));
        var frozen = Posted().ToSubmission("original") with { ProfileCompletion = operation };
        var checkpoint = new InstantQuotationSubmissionCheckpoint(new string('a', 64), 717,
            InstantQuotationSubmissionCheckpointStatus.Persisted, "digest", FrozenCustomer: frozen);
        var service = new InstantQuotationAuthenticatedPreparationService(new Sessions(), new Store(checkpoint), profiles, new Countries());
        var result = await service.PrepareAsync("session", "customer:7", Posted() with { FirstName = "forged" }, "changed", default);
        Assert.Same(frozen, result.Customer);
        Assert.Same(operation, result.Customer?.ProfileCompletion);
        Assert.Equal(717, result.TerminalResult?.RequestReference);
        Assert.Equal(0, profiles.Reads);
        Assert.False(result.SafeToRetry);
    }

    [Fact]
    public async Task MissingCapability_RejectsBeforeAnyDurableCheckpointAndCanRetry()
    {
        var profiles = new Profiles { Available = false };
        var service = new InstantQuotationAuthenticatedPreparationService(new Sessions(), new Store(), profiles, new Countries());
        var result = await service.PrepareAsync("session", "customer:7", Posted(), null, default);
        Assert.Null(result.Customer);
        Assert.Null(result.TerminalResult);
        Assert.True(result.SafeToRetry);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, result.ProblemCategory);
    }

    private static InstantQuotationProfileDetails Posted() => new()
    {
        FirstName = "Posted",
        LastName = "Name",
        Email = "posted@example.test",
        Mobile = "0800000000",
        Country = "Thailand",
        BillingStreet1 = "Billing",
        BillingCity = "Bangkok",
        BillingProvince = "Bangkok",
        BillingPostalCode = "10110",
        ShippingCountry = "Thailand",
    };

    private sealed class Profiles : IInstantQuotationProfileCompletionClient
    {
        public int Reads { get; private set; }
        public bool Available { get; init; } = true;
        public Task<InstantQuotationProfileGraphResult> ReadAsync(string ownerIdentity, CancellationToken cancellationToken)
        {
            Reads++;
            var billing = new CustomerAddress(10, null, "Stored billing", null, "Bangkok", "Bangkok", "10110", 66, null, null);
            var shipping = billing with { Id = 20, AddressLine1 = "Stored shipping" };
            var customer = new CustomerAccountDetails(7, "Owner", "Name", "Owner Name", null, null, null, "owner@example.test",
                null, null, 10, 20, null, null, billing, null, shipping);
            return Task.FromResult(new InstantQuotationProfileGraphResult(Available ? new(customer, "\"" + new string('a', 64) + "\"") : null,
                Available ? InstantQuotationProblemCategory.None : InstantQuotationProblemCategory.DependencyUnavailable));
        }
        public Task<InstantQuotationProfileCompletionResult> CompleteAsync(string ownerIdentity, InstantQuotationProfileCompletionOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Countries : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>([new(66, "Thailand", null, null, null, null, null, null)], true));
    }
    private sealed class Sessions : IInstantQuotationSessionStore
    {
        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            Task.FromResult<InstantQuotationSessionState?>(new(sessionId, new string('a', 64), new([]), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState requestState, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Store(InstantQuotationSubmissionCheckpoint? checkpoint = null) : IInstantQuotationSubmissionStore
    {
        public Task<IInstantQuotationSubmissionLease?> TryAcquireAsync(string submissionId, string ownerIdentity, CancellationToken cancellationToken) =>
            Task.FromResult<IInstantQuotationSubmissionLease?>(new Lease(checkpoint));
    }
    private sealed class Lease(InstantQuotationSubmissionCheckpoint? checkpoint) : IInstantQuotationSubmissionLease
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<bool> RenewAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<InstantQuotationSubmissionCheckpointRead> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(new InstantQuotationSubmissionCheckpointRead(true, checkpoint));
        public Task<bool> TryPutAsync(InstantQuotationSubmissionCheckpoint value, InstantQuotationSubmissionCheckpointStatus? expectedPriorStatus, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
