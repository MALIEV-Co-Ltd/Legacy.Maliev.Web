using System.Security.Cryptography;
using System.Text;

namespace Legacy.Maliev.Web.Application;

internal sealed class InstantQuotationFulfillmentCoordinator(IInstantQuotationFulfillmentClient client, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    internal async Task<InstantQuotationSubmissionResult> FulfillAsync(
        InstantQuotationSessionState session,
        InstantQuotationOrderQuote quote,
        string? ownerIdentity,
        InstantQuotationCustomerSubmission customer,
        IInstantQuotationSubmissionLease lease,
        InstantQuotationSubmissionCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        if (checkpoint.FinalizedFiles is not { Count: > 0 } files)
        {
            return Partial(checkpoint, InstantQuotationProblemCategory.Unexpected);
        }

        if (checkpoint.Status == InstantQuotationSubmissionCheckpointStatus.OrdersProvisioning
            && checkpoint.CompensationRequired)
        {
            await TryCompensateAsync(client, lease, checkpoint, cancellationToken);
            return Partial(checkpoint, InstantQuotationProblemCategory.Unexpected);
        }

        if (checkpoint.Status < InstantQuotationSubmissionCheckpointStatus.CustomerProvisioned)
        {
            if (!await HasWriteFenceAsync(lease, cancellationToken))
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            }

            var provisioned = await client.ProvisionCustomerAsync(ownerIdentity, customer, cancellationToken);
            if (!provisioned.ServiceAvailable)
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.DependencyUnavailable);
            }

            if (!provisioned.Authorized)
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Authorization);
            }

            if (provisioned.CustomerId is not > 0)
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Unexpected);
            }

            var next = checkpoint with
            {
                Status = InstantQuotationSubmissionCheckpointStatus.CustomerProvisioned,
                CustomerId = provisioned.CustomerId,
                CustomerCreated = provisioned.CustomerCreated,
            };
            if (!await TryAdvanceAsync(lease, checkpoint, next, cancellationToken))
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            }

            checkpoint = next;
        }

        if (checkpoint.Status < InstantQuotationSubmissionCheckpointStatus.OrdersProvisioning)
        {
            var next = checkpoint with
            {
                Status = InstantQuotationSubmissionCheckpointStatus.OrdersProvisioning,
                OrderIds = [],
            };
            if (!await TryAdvanceAsync(lease, checkpoint, next, cancellationToken))
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            }

            checkpoint = next;
        }

        if (checkpoint.Status == InstantQuotationSubmissionCheckpointStatus.OrdersProvisioning)
        {
            var orderIds = checkpoint.OrderIds?.ToList() ?? [];
            if (orderIds.Count > session.Parts.Count)
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Unexpected);
            }

            for (var index = orderIds.Count; index < session.Parts.Count; index++)
            {
                var part = session.Parts[index];
                if (!Guid.TryParseExact(part.UploadReference.Value, "D", out var fileId))
                {
                    return Partial(checkpoint, InstantQuotationProblemCategory.Unexpected);
                }

                var file = files.SingleOrDefault(item => item.FileId == fileId);
                if (file is null)
                {
                    return Partial(checkpoint, InstantQuotationProblemCategory.Unexpected);
                }

                if (!await HasWriteFenceAsync(lease, cancellationToken))
                {
                    return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
                }

                var order = await client.ProvisionOrderAsync(
                    session.SubmissionId,
                    index,
                    checkpoint.CustomerId!.Value,
                    customer.Description,
                    part,
                    quote.Parts[index],
                    quote.LeadTimeMaximumDays,
                    file,
                    cancellationToken);
                if (!order.ServiceAvailable)
                {
                    return Partial(checkpoint, InstantQuotationProblemCategory.DependencyUnavailable);
                }

                if (!order.Authorized)
                {
                    return Partial(checkpoint, InstantQuotationProblemCategory.Authorization);
                }

                if (order.Conflict)
                {
                    return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
                }

                if (!order.Succeeded || order.OrderId is not > 0)
                {
                    if (order.OrderId is > 0 && !orderIds.Contains(order.OrderId.Value))
                    {
                        orderIds.Add(order.OrderId.Value);
                    }

                    var compensation = checkpoint with
                    {
                        OrderIds = orderIds.ToArray(),
                        CompensationRequired = true,
                    };
                    if (!await TryAdvanceAsync(lease, checkpoint, compensation, cancellationToken))
                    {
                        return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
                    }

                    await TryCompensateAsync(client, lease, compensation, cancellationToken);
                    return Partial(checkpoint, InstantQuotationProblemCategory.Unexpected);
                }

                orderIds.Add(order.OrderId.Value);
                var next = checkpoint with { OrderIds = orderIds.ToArray() };
                if (!await TryAdvanceAsync(lease, checkpoint, next, cancellationToken))
                {
                    return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
                }

                checkpoint = next;
            }

            var ordersProvisioned = checkpoint with
            {
                Status = InstantQuotationSubmissionCheckpointStatus.OrdersProvisioned,
            };
            if (!await TryAdvanceAsync(lease, checkpoint, ordersProvisioned, cancellationToken))
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            }

            checkpoint = ordersProvisioned;
        }

        if (checkpoint.Status < InstantQuotationSubmissionCheckpointStatus.IdentityPrepared)
        {
            var next = checkpoint with
            {
                Status = InstantQuotationSubmissionCheckpointStatus.IdentityPrepared,
                TemporaryPassword = ownerIdentity is null ? GenerateTemporaryPassword() : null,
            };
            if (!await TryAdvanceAsync(lease, checkpoint, next, cancellationToken))
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            }

            checkpoint = next;
        }

        if (checkpoint.Status < InstantQuotationSubmissionCheckpointStatus.IdentityProvisioned)
        {
            var identityCreated = false;
            if (ownerIdentity is null)
            {
                if (!await HasWriteFenceAsync(lease, cancellationToken))
                {
                    return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
                }

                var identity = await client.ProvisionIdentityAsync(
                    checkpoint.CustomerId!.Value,
                    customer,
                    checkpoint.TemporaryPassword!,
                    cancellationToken);
                if (!identity.Authorized)
                {
                    return Partial(checkpoint, InstantQuotationProblemCategory.Authorization);
                }

                // Auth availability must not discard a persisted manufacturing request.
                // Record the attempted stage and allow staff-visible order creation to continue.
                identityCreated = identity.ServiceAvailable && identity.IdentityCreated;
            }

            var next = checkpoint with
            {
                Status = InstantQuotationSubmissionCheckpointStatus.IdentityProvisioned,
                IdentityCreated = identityCreated,
            };
            if (!await TryAdvanceAsync(lease, checkpoint, next, cancellationToken))
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            }

            checkpoint = next;
        }

        if (checkpoint.Status == InstantQuotationSubmissionCheckpointStatus.IdentityProvisioned
            && checkpoint.IdentityCreated)
        {
            if (!await HasWriteFenceAsync(lease, cancellationToken))
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            }

            var prepared = await client.PrepareWelcomeAsync(customer, cancellationToken);
            if (!prepared.ServiceAvailable)
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.DependencyUnavailable);
            }

            if (!prepared.Authorized)
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Authorization);
            }

            if (string.IsNullOrWhiteSpace(prepared.ConfirmationToken))
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Unexpected);
            }

            var next = checkpoint with
            {
                Status = InstantQuotationSubmissionCheckpointStatus.WelcomePrepared,
                WelcomeConfirmationToken = prepared.ConfirmationToken,
            };
            if (!await TryAdvanceAsync(lease, checkpoint, next, cancellationToken))
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            }

            checkpoint = next;
        }

        if (checkpoint.Status == InstantQuotationSubmissionCheckpointStatus.WelcomePrepared
            && checkpoint.IdentityCreated)
        {
            if (!await HasWriteFenceAsync(lease, cancellationToken))
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            }

            var notification = await client.SendWelcomeAsync(
                customer,
                checkpoint.TemporaryPassword!,
                checkpoint.WelcomeConfirmationToken!,
                CreateOperationId(session.SubmissionId, "welcome"),
                cancellationToken);
            if (!notification.ServiceAvailable)
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.DependencyUnavailable);
            }

            if (!notification.Authorized)
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Authorization);
            }

            if (!notification.Sent)
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Unexpected);
            }

            var next = checkpoint with
            {
                Status = InstantQuotationSubmissionCheckpointStatus.WelcomeNotificationSent,
            };
            if (!await TryAdvanceAsync(lease, checkpoint, next, cancellationToken))
            {
                return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            }

            checkpoint = next;
        }

        if (checkpoint.QuotationNotifications is null)
        {
            if (!await HasWriteFenceAsync(lease, cancellationToken)) return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            var prepared = await client.PrepareQuotationNotificationsAsync(
                session, quote, customer, checkpoint.RequestReference, files, cancellationToken);
            if (!prepared.ServiceAvailable) return Partial(checkpoint, InstantQuotationProblemCategory.DependencyUnavailable);
            if (!prepared.Authorized) return Partial(checkpoint, InstantQuotationProblemCategory.Authorization);
            if (prepared.Payload is null) return Partial(checkpoint, InstantQuotationProblemCategory.Unexpected);
            var next = checkpoint with { QuotationNotifications = prepared.Payload.Snapshot() };
            if (!await TryAdvanceAsync(lease, checkpoint, next, cancellationToken)) return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            checkpoint = next;
        }

        var payload = checkpoint.QuotationNotifications;
        if (payload is null) return Partial(checkpoint, InstantQuotationProblemCategory.Unexpected);
        var notificationFailure = InstantQuotationProblemCategory.None;
        foreach (var customerMessage in new[] { true, false })
        {
            if (customerMessage ? checkpoint.CustomerQuotationNotificationSent : checkpoint.ManufacturingQuotationNotificationSent) continue;
            cancellationToken.ThrowIfCancellationRequested();
            // FileService's existing contract guarantees at least one hour. Preparation freezes a 45-minute bound.
            // Expired payloads fail closed; never regenerate signed bodies under the same operation identity.
            if (payload.ExpiresAt <= clock.GetUtcNow())
                return Partial(checkpoint, InstantQuotationProblemCategory.Unexpected);
            if (!await HasWriteFenceAsync(lease, cancellationToken)) return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            var notification = await client.SendQuotationNotificationAsync(
                customerMessage ? payload.Customer : payload.Manufacturing,
                CreateOperationId(session.SubmissionId, customerMessage ? "quotation-customer" : "quotation-manufacturing"), cancellationToken);
            if (!notification.Sent || !notification.ServiceAvailable || !notification.Authorized)
            {
                if (notificationFailure == InstantQuotationProblemCategory.None)
                    notificationFailure = !notification.Authorized ? InstantQuotationProblemCategory.Authorization
                        : !notification.ServiceAvailable ? InstantQuotationProblemCategory.DependencyUnavailable : InstantQuotationProblemCategory.Unexpected;
                continue;
            }
            var next = customerMessage ? checkpoint with { CustomerQuotationNotificationSent = true }
                : checkpoint with { ManufacturingQuotationNotificationSent = true };
            if (!await TryAdvanceAsync(lease, checkpoint, next, cancellationToken)) return Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
            checkpoint = next;
        }
        if (notificationFailure != InstantQuotationProblemCategory.None) return Partial(checkpoint, notificationFailure);

        var completed = checkpoint with
        {
            Status = InstantQuotationSubmissionCheckpointStatus.Completed,
            TemporaryPassword = null,
            WelcomeConfirmationToken = null,
        };
        return await TryAdvanceAsync(lease, checkpoint, completed, cancellationToken)
            ? new InstantQuotationSubmissionResult(
                InstantQuotationSubmissionOutcome.Completed,
                checkpoint.RequestReference,
                InstantQuotationProblemCategory.None,
                checkpoint.TransactionId,
                checkpoint.JourneyId)
            : Partial(checkpoint, InstantQuotationProblemCategory.Conflict);
    }

    internal static Guid CreateOperationId(string submissionId, string purpose)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}:{submissionId.ToLowerInvariant()}"));
        return new Guid(digest.AsSpan(0, 16));
    }

    internal static string GenerateTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@$%";
        while (true)
        {
            var characters = new char[20];
            for (var index = 0; index < characters.Length; index++)
            {
                characters[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }

            var password = new string(characters);
            if (password.Any(char.IsUpper)
                && password.Any(char.IsLower)
                && password.Any(char.IsDigit)
                && password.Any(character => !char.IsLetterOrDigit(character)))
            {
                return password;
            }
        }
    }

    private static async Task<bool> TryAdvanceAsync(
        IInstantQuotationSubmissionLease lease,
        InstantQuotationSubmissionCheckpoint prior,
        InstantQuotationSubmissionCheckpoint next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await lease.RenewAsync(cancellationToken)
                && await lease.TryPutAsync(next, prior.Status, cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException or HttpRequestException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<bool> HasWriteFenceAsync(
        IInstantQuotationSubmissionLease lease,
        CancellationToken cancellationToken)
    {
        try
        {
            return await lease.RenewAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException or HttpRequestException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<bool> TryCompensateAsync(
        IInstantQuotationFulfillmentClient client,
        IInstantQuotationSubmissionLease lease,
        InstantQuotationSubmissionCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        if (!await HasWriteFenceAsync(lease, cancellationToken))
        {
            return false;
        }

        foreach (var orderId in (checkpoint.OrderIds ?? []).Reverse())
        {
            if (!await client.CompensateOrderAsync(orderId, cancellationToken))
            {
                return false;
            }
        }

        if (checkpoint.CustomerCreated
            && checkpoint.CustomerId is int customerId
            && customerId > 0
            && !await client.CompensateCustomerAsync(customerId, cancellationToken))
        {
            return false;
        }

        var rolledBack = checkpoint with
        {
            Status = InstantQuotationSubmissionCheckpointStatus.FilesLinked,
            CustomerId = null,
            CustomerCreated = false,
            TemporaryPassword = null,
            IdentityCreated = false,
            OrderIds = null,
            WelcomeConfirmationToken = null,
            CompensationRequired = false,
        };
        return await TryAdvanceAsync(lease, checkpoint, rolledBack, cancellationToken);
    }

    private static InstantQuotationSubmissionResult Partial(
        InstantQuotationSubmissionCheckpoint checkpoint,
        InstantQuotationProblemCategory category) => new(
            InstantQuotationSubmissionOutcome.Partial,
            checkpoint.RequestReference,
            category,
            checkpoint.TransactionId,
            checkpoint.JourneyId);
}
