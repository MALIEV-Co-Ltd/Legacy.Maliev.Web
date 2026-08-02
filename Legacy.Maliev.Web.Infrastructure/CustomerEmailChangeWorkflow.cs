using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CustomerEmailChangeWorkflow(
    ICustomerAuthenticationClient authenticationClient,
    ICustomerAccountClient customerClient,
    ILogger<CustomerEmailChangeWorkflow> logger) : ICustomerEmailChangeWorkflow
{
    public async Task<CustomerEmailChangeWorkflowResult> CompleteAsync(
        string email,
        string token,
        CancellationToken cancellationToken)
    {
        var validation = await authenticationClient.ValidateEmailChangeAsync(
            email,
            token,
            cancellationToken);
        if (!validation.ServiceAvailable || !validation.Authorized)
        {
            return new(false, validation.ServiceAvailable, validation.Authorized);
        }

        if (!validation.Valid
            || validation.CustomerId is not > 0
            || string.IsNullOrWhiteSpace(validation.CurrentEmail)
            || string.IsNullOrWhiteSpace(validation.NewEmail))
        {
            return new(false, true, true);
        }

        var profileResult = await customerClient.GetProfileAsync(
            validation.CustomerId.Value,
            cancellationToken);
        if (!profileResult.ServiceAvailable || !profileResult.Authorized)
        {
            return new(false, profileResult.ServiceAvailable, profileResult.Authorized);
        }

        var profile = profileResult.Profile;
        if (profile is null || string.IsNullOrWhiteSpace(profile.Email))
        {
            return new(false, true, true);
        }

        var profileIsCurrent = string.Equals(
            profile.Email,
            validation.CurrentEmail,
            StringComparison.OrdinalIgnoreCase);
        var profileIsNew = string.Equals(
            profile.Email,
            validation.NewEmail,
            StringComparison.OrdinalIgnoreCase);
        if (!profileIsCurrent && !profileIsNew)
        {
            logger.LogWarning("Customer email confirmation was rejected because profile and identity state disagree.");
            return new(false, true, true);
        }

        if (validation.Completed)
        {
            if (profileIsCurrent)
            {
                var reconciliation = await customerClient.UpdateEmailAsync(
                    validation.CustomerId.Value,
                    validation.NewEmail,
                    cancellationToken);
                return new(
                    reconciliation.Succeeded,
                    reconciliation.ServiceAvailable,
                    reconciliation.Authorized);
            }

            return new(true, true, true);
        }

        if (!profileIsNew)
        {
            var profileUpdate = await customerClient.UpdateEmailAsync(
                validation.CustomerId.Value,
                validation.NewEmail,
                cancellationToken);
            if (!profileUpdate.Succeeded)
            {
                return new(false, profileUpdate.ServiceAvailable, profileUpdate.Authorized);
            }
        }

        var completion = await authenticationClient.CompleteEmailChangeAsync(
            email,
            token,
            cancellationToken);
        if (completion.Succeeded)
        {
            return new(true, true, true);
        }

        if (!completion.ServiceAvailable)
        {
            var outcome = await authenticationClient.ValidateEmailChangeAsync(
                email,
                token,
                cancellationToken);
            if (outcome.Valid && outcome.Completed)
            {
                return new(true, true, true);
            }

            logger.LogWarning("Customer email confirmation could not determine whether the identity change completed.");
            return new(false, false, completion.Authorized);
        }

        await RollBackProfileAsync(
            validation.CustomerId.Value,
            validation.CurrentEmail);
        return new(false, true, completion.Authorized);
    }

    private async Task RollBackProfileAsync(
        int customerId,
        string currentEmail)
    {
        try
        {
            var rollback = await customerClient.UpdateEmailAsync(
                customerId,
                currentEmail,
                CancellationToken.None);
            if (rollback.Succeeded)
            {
                return;
            }
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Customer email confirmation profile compensation failed.");
            return;
        }

        logger.LogCritical("Customer email confirmation profile compensation was rejected.");
    }
}
