namespace Legacy.Maliev.Web.Infrastructure;

internal static class InstantQuotationFileUploadLimits
{
    internal const long EdgeCompatibleBytes = 100L * 1024 * 1024;
    internal const long LegacyBytes = 200L * 1024 * 1024;

    internal static bool IsSupportedCapability(long bytes) =>
        bytes is EdgeCompatibleBytes or LegacyBytes;
}
