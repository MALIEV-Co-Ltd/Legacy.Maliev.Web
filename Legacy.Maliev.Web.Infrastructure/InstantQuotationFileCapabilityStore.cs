using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class InstantQuotationFileCapabilityStore(
    IDistributedCache cache,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider,
    ILogger<InstantQuotationFileCapabilityStore> logger) : IInstantQuotationFileCapabilityStore
{
    internal const int CurrentVersion = 1;
    internal const string CacheKeyPrefix = "legacy:web:instant-quotation-file-capability:";
    internal const string ProtectorPurpose = "Legacy.Maliev.Web.InstantQuotationFileCapability.v1";
    private const long ContractMaxUploadBytes = 209_715_200;
    private const int ContractMaxFilesPerSession = 100;
    private static readonly string[] ContractExtensions =
        [".stl", ".obj", ".3mf", ".step", ".stp", ".iges", ".igs", ".glb", ".gltf"];
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public async Task<bool> PutAsync(
        string webSessionId,
        string? ownerIdentity,
        InstantQuotationFileCapability capability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (!IsValidWebSessionId(webSessionId)
            || !IsValidOwner(ownerIdentity)
            || !IsValid(capability, timeProvider.GetUtcNow()))
        {
            return false;
        }

        var persisted = new PersistedCapability(
            CurrentVersion,
            webSessionId,
            ownerIdentity,
            capability.SessionId,
            capability.SessionToken,
            capability.ExpiresAt,
            capability.MaxUploadBytes,
            capability.MaxFilesPerSession,
            capability.SupportedExtensions.ToArray());
        var payload = JsonSerializer.SerializeToUtf8Bytes(persisted);
        await cache.SetAsync(
            Key(webSessionId),
            protector.Protect(payload),
            new DistributedCacheEntryOptions { AbsoluteExpiration = capability.ExpiresAt },
            cancellationToken);
        return true;
    }

    public async Task<InstantQuotationFileCapability?> GetAsync(
        string webSessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken)
    {
        if (!IsValidWebSessionId(webSessionId) || !IsValidOwner(ownerIdentity))
        {
            return null;
        }

        var persisted = await ReadAsync(webSessionId, cancellationToken);
        if (persisted is null || !OwnerMatches(persisted.OwnerIdentity, ownerIdentity))
        {
            return null;
        }

        return new InstantQuotationFileCapability(
            persisted.SessionId,
            persisted.SessionToken!,
            persisted.ExpiresAt,
            persisted.MaxUploadBytes,
            persisted.MaxFilesPerSession,
            persisted.SupportedExtensions!.ToArray());
    }

    public async Task<bool> RemoveAsync(
        string webSessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken)
    {
        if (!IsValidWebSessionId(webSessionId) || !IsValidOwner(ownerIdentity))
        {
            return false;
        }

        var persisted = await ReadAsync(webSessionId, cancellationToken);
        if (persisted is null || !OwnerMatches(persisted.OwnerIdentity, ownerIdentity))
        {
            return false;
        }

        await cache.RemoveAsync(Key(webSessionId), cancellationToken);
        return true;
    }

    private async Task<PersistedCapability?> ReadAsync(
        string webSessionId,
        CancellationToken cancellationToken)
    {
        var protectedPayload = await cache.GetAsync(Key(webSessionId), cancellationToken);
        if (protectedPayload is null)
        {
            return null;
        }

        try
        {
            var payload = protector.Unprotect(protectedPayload);
            var persisted = JsonSerializer.Deserialize<PersistedCapability>(payload);
            if (!IsValid(persisted, webSessionId, timeProvider.GetUtcNow()))
            {
                await cache.RemoveAsync(Key(webSessionId), cancellationToken);
                return null;
            }

            return persisted;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or NotSupportedException)
        {
            logger.LogWarning(exception, "Rejected an unreadable instant quotation file capability.");
            await cache.RemoveAsync(Key(webSessionId), cancellationToken);
            return null;
        }
    }

    private static bool IsValid(PersistedCapability? persisted, string webSessionId, DateTimeOffset now) =>
        persisted is not null
        && persisted.Version == CurrentVersion
        && string.Equals(persisted.WebSessionId, webSessionId, StringComparison.Ordinal)
        && IsValidOwner(persisted.OwnerIdentity)
        && IsValid(
            new InstantQuotationFileCapability(
                persisted.SessionId,
                persisted.SessionToken ?? string.Empty,
                persisted.ExpiresAt,
                persisted.MaxUploadBytes,
                persisted.MaxFilesPerSession,
                persisted.SupportedExtensions ?? []),
            now);

    private static bool IsValid(InstantQuotationFileCapability capability, DateTimeOffset now) =>
        capability.SessionId != Guid.Empty
        && capability.SessionToken is { Length: >= 32 and <= 512 }
        && capability.SessionToken.All(IsPrintableAscii)
        && capability.ExpiresAt > now
        && capability.MaxUploadBytes == ContractMaxUploadBytes
        && capability.MaxFilesPerSession == ContractMaxFilesPerSession
        && capability.SupportedExtensions is not null
        && capability.SupportedExtensions.SequenceEqual(ContractExtensions, StringComparer.Ordinal);

    private static bool IsValidWebSessionId(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsValidOwner(string? value) =>
        value is null || (value.Length <= 512 && !string.IsNullOrWhiteSpace(value));

    private static bool OwnerMatches(string? actual, string? expected) =>
        string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool IsPrintableAscii(char value) => value is >= '!' and <= '~';

    private static string Key(string webSessionId) => $"{CacheKeyPrefix}{webSessionId}";

    private sealed record PersistedCapability(
        int Version,
        string? WebSessionId,
        string? OwnerIdentity,
        Guid SessionId,
        string? SessionToken,
        DateTimeOffset ExpiresAt,
        long MaxUploadBytes,
        int MaxFilesPerSession,
        IReadOnlyList<string>? SupportedExtensions);
}
