using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationSubmissionTests
{
    private const string SessionId = "protected-session";
    private const string Owner = "protected-owner";
    private const string SubmissionId = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";

    [Fact]
    public async Task Submit_ValidCustomer_PersistsAuthoritativeRequestBeforeFinalizingUploads()
    {
        var events = new List<string>();
        var quotation = new RecordingQuotationClient(_ =>
            new QuotationRequestResult(417, ServiceAvailable: true, Authorized: true));
        var persisted = new RecordingSubmissionStore(events);
        var upload = new RecordingUploadClient(events, SuccessfulFinalization());
        var service = Service(quotation, persisted, upload, Session(Part(quantity: 2)));

        var result = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Completed, result.Outcome);
        Assert.Equal(417, result.RequestReference);
        Assert.Equal(["persisted", "finalize", "completed"], events);
        Assert.Equal(
            [null, InstantQuotationSubmissionCheckpointStatus.Persisted],
            persisted.ExpectedPriorStatuses);
        var call = Assert.Single(quotation.Calls);
        Assert.Equal($"legacy-web-instant-quotation-{SubmissionId.ToLowerInvariant()}", call.IdempotencyKey);
        Assert.Contains("Orders", call.Submission.Message, StringComparison.Ordinal);
        Assert.Contains("1 - bracket.stl", call.Submission.Message, StringComparison.Ordinal);
        Assert.Contains("Material: ABS", call.Submission.Message, StringComparison.Ordinal);
        Assert.Contains("Quantity: 2 piece(s)", call.Submission.Message, StringComparison.Ordinal);
        Assert.Contains("Total price:", call.Submission.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("opaque-upload-reference", call.Submission.Message, StringComparison.Ordinal);
        Assert.Equal(["opaque-upload-reference"], upload.UploadReferences.Select(item => item.Value));
        Assert.DoesNotContain(upload.OperationIds.Single(), call.Submission.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_RetryAfterPartialFinalization_ReusesPersistedRequestAndDeterministicOperation()
    {
        var quotation = new RecordingQuotationClient(_ =>
            new QuotationRequestResult(417, ServiceAvailable: true, Authorized: true));
        var persisted = new RecordingSubmissionStore();
        var upload = new RecordingUploadClient(
            null,
            InstantQuotationFinalizationResult.Unavailable("ignored"),
            SuccessfulFinalization());
        var service = Service(quotation, persisted, upload, Session(Part()));

        var first = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);
        var retry = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, first.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, first.ProblemCategory);
        Assert.Equal(InstantQuotationSubmissionOutcome.Completed, retry.Outcome);
        Assert.Single(quotation.Calls);
        Assert.Equal(2, upload.OperationIds.Count);
        Assert.Equal(upload.OperationIds[0], upload.OperationIds[1]);
        Assert.DoesNotContain(upload.OperationIds[0], retry.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_AlreadyCompleted_IsIdempotentWithoutDownstreamCalls()
    {
        var persisted = new RecordingSubmissionStore();
        var quotation = new RecordingQuotationClient(_ => new QuotationRequestResult(417, true, true));
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var service = Service(quotation, persisted, upload, Session(Part()));

        var initial = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);
        var result = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Completed, initial.Outcome);
        Assert.Equal(InstantQuotationSubmissionOutcome.Completed, result.Outcome);
        Assert.Equal(417, result.RequestReference);
        Assert.Single(quotation.Calls);
        Assert.Single(upload.OperationIds);
    }

    [Fact]
    public async Task Submit_EmptyQuote_FailsValidationBeforeDownstreamCalls()
    {
        var quotation = new RecordingQuotationClient(_ => throw new InvalidOperationException());
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var service = Service(quotation, new RecordingSubmissionStore(), upload, Session());

        var result = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Rejected, result.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.Validation, result.ProblemCategory);
        Assert.Empty(quotation.Calls);
        Assert.Empty(upload.OperationIds);
    }

    [Theory]
    [InlineData("", "Ev", "mali@example.com", "Thailand")]
    [InlineData("Mali", "", "mali@example.com", "Thailand")]
    [InlineData("Mali", "Ev", "not-an-email", "Thailand")]
    [InlineData("Mali", "Ev", "mali@example.com", "")]
    public async Task Submit_InvalidCustomer_FailsValidation(
        string firstName,
        string lastName,
        string email,
        string country)
    {
        var quotation = new RecordingQuotationClient(_ => throw new InvalidOperationException());
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var service = Service(quotation, new RecordingSubmissionStore(), upload, Session(Part()));

        var result = await service.SubmitAsync(
            SessionId,
            Owner,
            Customer(firstName, lastName, email, country),
            CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Rejected, result.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.Validation, result.ProblemCategory);
        Assert.Empty(quotation.Calls);
        Assert.Empty(upload.OperationIds);
    }

    [Fact]
    public async Task Submit_MissingTelephoneOrOversizedDescription_FailsValidation()
    {
        var quotation = new RecordingQuotationClient(_ => throw new InvalidOperationException());
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var service = Service(quotation, new RecordingSubmissionStore(), upload, Session(Part()));

        var missingTelephone = await service.SubmitAsync(
            SessionId,
            Owner,
            Customer(telephoneNumber: ""),
            CancellationToken.None);
        var oversizedDescription = await service.SubmitAsync(
            SessionId,
            Owner,
            Customer(description: new string('x', 513)),
            CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Validation, missingTelephone.ProblemCategory);
        Assert.Equal(InstantQuotationProblemCategory.Validation, oversizedDescription.ProblemCategory);
        Assert.Empty(quotation.Calls);
        Assert.Empty(upload.OperationIds);
    }

    [Theory]
    [InlineData("firstName")]
    [InlineData("lastName")]
    [InlineData("email")]
    [InlineData("telephone")]
    [InlineData("country")]
    [InlineData("company")]
    [InlineData("tax")]
    public async Task Submit_ContactFieldOverFiftyCharacters_FailsValidation(string field)
    {
        var value = new string('x', 51);
        var customer = Customer(
            firstName: field == "firstName" ? value : "Mali",
            lastName: field == "lastName" ? value : "Ev",
            email: field == "email" ? $"{new string('x', 39)}@example.com" : "mali@example.com",
            country: field == "country" ? value : "Thailand",
            telephoneNumber: field == "telephone" ? value : "020000000",
            companyName: field == "company" ? value : "MALIEV",
            taxIdentification: field == "tax" ? value : "0100000000000");
        var quotation = new RecordingQuotationClient(_ => throw new InvalidOperationException());
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var service = Service(quotation, new RecordingSubmissionStore(), upload, Session(Part()));

        var result = await service.SubmitAsync(SessionId, Owner, customer, CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Validation, result.ProblemCategory);
        Assert.Empty(quotation.Calls);
        Assert.Empty(upload.OperationIds);
    }

    [Theory]
    [InlineData("different-submission", 417, InstantQuotationProblemCategory.Conflict)]
    [InlineData(SubmissionId, 0, InstantQuotationProblemCategory.Unexpected)]
    public async Task Submit_InvalidRecoveredCheckpoint_FailsClosedBeforeFinalization(
        string checkpointSubmissionId,
        int requestReference,
        InstantQuotationProblemCategory expectedProblem)
    {
        var persisted = new RecordingSubmissionStore
        {
            ReadOverride = new InstantQuotationSubmissionCheckpoint(
                checkpointSubmissionId,
                requestReference,
                InstantQuotationSubmissionCheckpointStatus.Persisted,
                new string('0', 64)),
        };
        var quotation = new RecordingQuotationClient(_ => throw new InvalidOperationException());
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var service = Service(quotation, persisted, upload, Session(Part()));

        var result = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Rejected, result.Outcome);
        Assert.Equal(expectedProblem, result.ProblemCategory);
        Assert.Empty(quotation.Calls);
        Assert.Empty(upload.OperationIds);
    }

    [Fact]
    public async Task Submit_UndefinedRecoveredCheckpointStatus_FailsClosedBeforeFinalization()
    {
        var persisted = new RecordingSubmissionStore
        {
            ReadOverride = new InstantQuotationSubmissionCheckpoint(
                SubmissionId,
                417,
                (InstantQuotationSubmissionCheckpointStatus)99,
                new string('0', 64)),
        };
        var quotation = new RecordingQuotationClient(_ => throw new InvalidOperationException());
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var service = Service(quotation, persisted, upload, Session(Part()));

        var result = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Rejected, result.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
        Assert.Empty(upload.OperationIds);
    }

    [Theory]
    [InlineData(InstantQuotationServiceStatus.Unavailable, InstantQuotationAuthorizationStatus.Authorized, InstantQuotationProblemCategory.DependencyUnavailable)]
    [InlineData(InstantQuotationServiceStatus.Available, InstantQuotationAuthorizationStatus.Denied, InstantQuotationProblemCategory.Authorization)]
    public async Task Submit_FinalizationSuccessStatusStillRequiresAvailableAuthorizedBoundary(
        InstantQuotationServiceStatus serviceStatus,
        InstantQuotationAuthorizationStatus authorizationStatus,
        InstantQuotationProblemCategory expectedProblem)
    {
        var finalization = new InstantQuotationFinalizationResult(
            "ignored",
            serviceStatus,
            authorizationStatus,
            InstantQuotationOperationStatus.Succeeded,
            InstantQuotationProblemCategory.None);
        var service = Service(
            new RecordingQuotationClient(_ => new QuotationRequestResult(417, true, true)),
            new RecordingSubmissionStore(),
            new RecordingUploadClient(null, finalization),
            Session(Part()));

        var result = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
        Assert.Equal(expectedProblem, result.ProblemCategory);
    }

    [Fact]
    public async Task Submit_FinalizationSuccessWithMismatchedOperationId_RemainsPartial()
    {
        var upload = new RecordingUploadClient(null, SuccessfulFinalization() with { OperationId = "forged" })
        {
            PreserveResultOperationId = true,
        };
        var service = Service(
            new RecordingQuotationClient(_ => new QuotationRequestResult(417, true, true)),
            new RecordingSubmissionStore(),
            upload,
            Session(Part()));

        var result = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
    }

    [Fact]
    public async Task Submit_FinalizationSucceededWithProblem_RemainsPartial()
    {
        var finalization = SuccessfulFinalization() with
        {
            ProblemCategory = InstantQuotationProblemCategory.Conflict,
        };
        var service = Service(
            new RecordingQuotationClient(_ => new QuotationRequestResult(417, true, true)),
            new RecordingSubmissionStore(),
            new RecordingUploadClient(null, finalization),
            Session(Part()));

        var result = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.Conflict, result.ProblemCategory);
    }

    [Fact]
    public async Task Submit_PersistedRetryWithMutatedPartSnapshot_FailsConflictWithoutRetryingFinalization()
    {
        var quotation = new RecordingQuotationClient(_ => new QuotationRequestResult(417, true, true));
        var persisted = new RecordingSubmissionStore();
        var upload = new RecordingUploadClient(
            null,
            InstantQuotationFinalizationResult.Unavailable("ignored"),
            SuccessfulFinalization());
        var sessions = new RecordingSessionStore(Session(Part()), Owner);
        var service = new InstantQuotationSubmissionService(
            sessions,
            new InstantQuotationPricingService(),
            quotation,
            persisted,
            upload);

        var initial = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);
        sessions.CurrentSession = Session(Part(quantity: 3) with
        {
            UploadReference = new InstantQuotationUploadReference("different-upload-reference"),
        });
        var retry = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, initial.Outcome);
        Assert.Equal(InstantQuotationSubmissionOutcome.Rejected, retry.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.Conflict, retry.ProblemCategory);
        Assert.Single(quotation.Calls);
        Assert.Single(upload.OperationIds);
    }

    [Fact]
    public async Task Submit_ConcurrentCalls_AreSerializedBeforeCreatingRequestOrFinalizing()
    {
        var quotationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQuotation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var quotation = new RecordingQuotationClient(_ => new QuotationRequestResult(417, true, true))
        {
            BeforeReturnAsync = async () =>
            {
                quotationEntered.TrySetResult();
                await releaseQuotation.Task;
            },
        };
        var persisted = new RecordingSubmissionStore();
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var service = Service(quotation, persisted, upload, Session(Part()));

        var first = service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);
        await quotationEntered.Task;
        var concurrent = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);
        releaseQuotation.SetResult();
        var completed = await first;

        Assert.Equal(InstantQuotationSubmissionOutcome.Rejected, concurrent.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.Conflict, concurrent.ProblemCategory);
        Assert.Equal(InstantQuotationSubmissionOutcome.Completed, completed.Outcome);
        Assert.Single(quotation.Calls);
        Assert.Single(upload.OperationIds);
    }

    [Fact]
    public async Task Submit_LeaseTakeoverWhileQuotationIsInFlight_PreventsStaleCheckpointAndFinalization()
    {
        var quotationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQuotation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var quotation = new RecordingQuotationClient(_ => new QuotationRequestResult(417, true, true))
        {
            BeforeReturnAsync = async () =>
            {
                quotationEntered.TrySetResult();
                await releaseQuotation.Task;
            },
        };
        var persisted = new RecordingSubmissionStore();
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var service = Service(quotation, persisted, upload, Session(Part()));

        var staleSubmission = service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);
        await quotationEntered.Task;
        persisted.ForceLeaseTakeover();
        releaseQuotation.SetResult();
        var result = await staleSubmission;

        Assert.Equal(InstantQuotationSubmissionOutcome.Rejected, result.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.Conflict, result.ProblemCategory);
        Assert.Single(quotation.Calls);
        Assert.Null(persisted.Checkpoint);
        Assert.Empty(upload.OperationIds);
    }

    [Theory]
    [InlineData(false, true, InstantQuotationProblemCategory.DependencyUnavailable)]
    [InlineData(true, false, InstantQuotationProblemCategory.Authorization)]
    [InlineData(true, true, InstantQuotationProblemCategory.Unexpected)]
    public async Task Submit_QuotationFailure_FailsClosedWithoutPersistenceOrFinalization(
        bool serviceAvailable,
        bool authorized,
        InstantQuotationProblemCategory expectedProblem)
    {
        var quotation = new RecordingQuotationClient(_ =>
            new QuotationRequestResult(null, serviceAvailable, authorized));
        var persisted = new RecordingSubmissionStore();
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var service = Service(quotation, persisted, upload, Session(Part()));

        var result = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Rejected, result.Outcome);
        Assert.Equal(expectedProblem, result.ProblemCategory);
        Assert.Null(persisted.Checkpoint);
        Assert.Empty(upload.OperationIds);
    }

    [Fact]
    public async Task Submit_QuotationTimeout_FailsClosedWithoutPersistenceOrFinalization()
    {
        var quotation = new RecordingQuotationClient(_ => throw new TimeoutException());
        var persisted = new RecordingSubmissionStore();
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var service = Service(quotation, persisted, upload, Session(Part()));

        var result = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Rejected, result.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, result.ProblemCategory);
        Assert.Null(persisted.Checkpoint);
        Assert.Empty(upload.OperationIds);
    }

    [Fact]
    public async Task Submit_PersistenceConflict_FailsClosedBeforeFinalization()
    {
        var quotation = new RecordingQuotationClient(_ =>
            new QuotationRequestResult(417, ServiceAvailable: true, Authorized: true));
        var persisted = new RecordingSubmissionStore { AcceptWrites = false };
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var service = Service(quotation, persisted, upload, Session(Part()));

        var result = await service.SubmitAsync(SessionId, Owner, Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Rejected, result.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.Conflict, result.ProblemCategory);
        Assert.Empty(upload.OperationIds);
    }

    [Fact]
    public async Task Submit_OwnerMismatch_FailsClosedBeforePricingOrDownstreamCalls()
    {
        var quotation = new RecordingQuotationClient(_ => throw new InvalidOperationException());
        var persisted = new RecordingSubmissionStore();
        var upload = new RecordingUploadClient(null, SuccessfulFinalization());
        var sessions = new RecordingSessionStore(Session(Part()), Owner);
        var pricing = new RecordingPricingService();
        var service = new InstantQuotationSubmissionService(sessions, pricing, quotation, persisted, upload);

        var result = await service.SubmitAsync(SessionId, "different-owner", Customer(), CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Rejected, result.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.Authorization, result.ProblemCategory);
        Assert.Equal(0, pricing.CallCount);
        Assert.Empty(quotation.Calls);
        Assert.Empty(upload.OperationIds);
    }

    private static InstantQuotationSubmissionService Service(
        RecordingQuotationClient quotation,
        RecordingSubmissionStore persisted,
        RecordingUploadClient upload,
        InstantQuotationSessionState session) => new(
            new RecordingSessionStore(session, Owner),
            new InstantQuotationPricingService(),
            quotation,
            persisted,
            upload);

    private static InstantQuotationCustomerSubmission Customer(
        string firstName = "Mali",
        string lastName = "Ev",
        string email = "mali@example.com",
        string country = "Thailand",
        string telephoneNumber = "020000000",
        string description = "Please verify the final manufacturability.",
        string companyName = "MALIEV",
        string taxIdentification = "0100000000000") => new(
            firstName,
            lastName,
            email,
            telephoneNumber,
            country,
            companyName,
            taxIdentification,
            description);

    private static InstantQuotationSessionState Session(params InstantQuotationPart[] parts) => new(
        SessionId,
        SubmissionId,
        new InstantQuotationOrderState(parts),
        DateTimeOffset.Parse("2026-07-19T00:00:00+07:00"),
        DateTimeOffset.Parse("2026-07-19T00:00:00+07:00"));

    private static InstantQuotationPart Part(int quantity = 1) => new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        "bracket.stl",
        new InstantQuotationUploadReference("opaque-upload-reference"),
        AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
            10,
            1_000,
            200,
            [200, 200],
            [60, 60],
            12,
            1,
            true),
        new InstantQuotationPartConfiguration("ABS", "Black", quantity));

    private static InstantQuotationFinalizationResult SuccessfulFinalization() => new(
        "ignored",
        InstantQuotationServiceStatus.Available,
        InstantQuotationAuthorizationStatus.Authorized,
        InstantQuotationOperationStatus.Succeeded,
        InstantQuotationProblemCategory.None);

    private sealed class RecordingSessionStore(
        InstantQuotationSessionState session,
        string owner) : IInstantQuotationSessionStore
    {
        public InstantQuotationSessionState CurrentSession { get; set; } = session;

        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState requestState, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            Task.FromResult<InstantQuotationSessionState?>(
                sessionId == CurrentSession.SessionId && ownerIdentity == owner ? CurrentSession : null);

        public Task<bool> PutAsync(InstantQuotationSessionState value, string? ownerIdentity, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingPricingService : IInstantQuotationPricingService
    {
        private readonly InstantQuotationPricingService inner = new();

        public int CallCount { get; private set; }

        public InstantQuotationOrderQuote Quote(InstantQuotationOrderState state)
        {
            CallCount++;
            return inner.Quote(state);
        }
    }

    private sealed class RecordingQuotationClient(
        Func<QuotationRequestSubmission, QuotationRequestResult> result) : IQuotationClient
    {
        public List<QuotationCall> Calls { get; } = [];

        public Func<Task>? BeforeReturnAsync { get; init; }

        public async Task<QuotationRequestResult> CreateRequestAsync(
            QuotationRequestSubmission submission,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            Calls.Add(new QuotationCall(submission, idempotencyKey));
            if (BeforeReturnAsync is not null)
            {
                await BeforeReturnAsync();
            }

            return result(submission);
        }
    }

    private sealed record QuotationCall(
        QuotationRequestSubmission Submission,
        string IdempotencyKey);

    private sealed class RecordingSubmissionStore(List<string>? events = null) : IInstantQuotationSubmissionStore
    {
        private readonly object sync = new();
        private InstantQuotationSubmissionCheckpoint? checkpoint;
        private long generation;
        private bool leased;

        public bool AcceptWrites { get; init; } = true;

        public InstantQuotationSubmissionCheckpoint? ReadOverride { get; init; }

        public InstantQuotationSubmissionCheckpoint? Checkpoint
        {
            get
            {
                lock (sync)
                {
                    return checkpoint;
                }
            }
        }

        public List<InstantQuotationSubmissionCheckpointStatus?> ExpectedPriorStatuses { get; } = [];

        public Task<IInstantQuotationSubmissionLease?> TryAcquireAsync(
            string submissionId,
            string ownerIdentity,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                if (leased)
                {
                    return Task.FromResult<IInstantQuotationSubmissionLease?>(null);
                }

                leased = true;
                generation++;
                return Task.FromResult<IInstantQuotationSubmissionLease?>(
                    new RecordingLease(this, generation));
            }
        }

        public void ForceLeaseTakeover()
        {
            lock (sync)
            {
                generation++;
                leased = true;
            }
        }

        private Task<InstantQuotationSubmissionCheckpointRead> ReadAsync(
            long leaseGeneration,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var valid = leased && generation == leaseGeneration;
                return Task.FromResult(new InstantQuotationSubmissionCheckpointRead(
                    valid,
                    valid ? ReadOverride ?? checkpoint : null));
            }
        }

        private Task<bool> TryPutAsync(
            long leaseGeneration,
            InstantQuotationSubmissionCheckpoint value,
            InstantQuotationSubmissionCheckpointStatus? expectedPriorStatus,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                ExpectedPriorStatuses.Add(expectedPriorStatus);
                var validLease = leased && generation == leaseGeneration;
                var matchesPrior = expectedPriorStatus is null
                    ? checkpoint is null
                    : checkpoint?.Status == expectedPriorStatus;
                if (!validLease || !AcceptWrites || !matchesPrior)
                {
                    return Task.FromResult(false);
                }

                checkpoint = value;
                events?.Add(value.Status == InstantQuotationSubmissionCheckpointStatus.Persisted ? "persisted" : "completed");
                return Task.FromResult(true);
            }
        }

        private sealed class RecordingLease(
            RecordingSubmissionStore store,
            long leaseGeneration) : IInstantQuotationSubmissionLease
        {
            public Task<InstantQuotationSubmissionCheckpointRead> ReadAsync(CancellationToken cancellationToken) =>
                store.ReadAsync(leaseGeneration, cancellationToken);

            public Task<bool> TryPutAsync(
                InstantQuotationSubmissionCheckpoint checkpoint,
                InstantQuotationSubmissionCheckpointStatus? expectedPriorStatus,
                CancellationToken cancellationToken) =>
                store.TryPutAsync(leaseGeneration, checkpoint, expectedPriorStatus, cancellationToken);

            public ValueTask DisposeAsync()
            {
                lock (store.sync)
                {
                    if (store.generation == leaseGeneration)
                    {
                        store.leased = false;
                    }
                }

                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RecordingUploadClient(
        List<string>? events,
        params InstantQuotationFinalizationResult[] results) : IInstantQuotationUploadClient
    {
        private readonly Queue<InstantQuotationFinalizationResult> results = new(results);

        public List<string> OperationIds { get; } = [];

        public IReadOnlyList<InstantQuotationUploadReference> UploadReferences { get; private set; } = [];

        public bool PreserveResultOperationId { get; init; }

        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, Stream content, string fileName, string contentType, long contentLength, string operationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, InstantQuotationUploadReference uploadReference, string operationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InstantQuotationFinalizationResult> FinalizeAsync(
            string sessionId,
            IReadOnlyList<InstantQuotationUploadReference> uploadReferences,
            string operationId,
            CancellationToken cancellationToken)
        {
            events?.Add("finalize");
            OperationIds.Add(operationId);
            UploadReferences = uploadReferences;
            var result = results.Dequeue();
            return Task.FromResult(PreserveResultOperationId ? result : result with { OperationId = operationId });
        }
    }
}
