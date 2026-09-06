namespace Legacy.Maliev.Web.Application;

/// <summary>Evaluates whether the CNC quotation route has its required rollout dependencies.</summary>
public static class CncQuotationAvailability
{
    /// <summary>The commercial calibration revision required by the migrated CNC planner.</summary>
    public const string CommercialRulesVersion = "cnc-commercial-v5";

    /// <summary>Requires receipt storage in every environment and approved atomic storage in production.</summary>
    public static bool IsAvailable(
        bool isDevelopment,
        bool isEnabled,
        string? approvedCommercialRulesVersion,
        bool receiptStoreAvailable,
        bool isSharedDistributedAtomic) =>
        receiptStoreAvailable
        && (isDevelopment
            || (isEnabled
                && string.Equals(approvedCommercialRulesVersion, CommercialRulesVersion, StringComparison.Ordinal)
                && isSharedDistributedAtomic));
}
