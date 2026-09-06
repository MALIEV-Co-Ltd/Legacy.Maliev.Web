using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncQuotationAvailabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsAvailable_ReceiptStoreMissing_DeniesEveryEnvironment(bool isDevelopment)
    {
        Assert.False(CncQuotationAvailability.IsAvailable(isDevelopment, true, "cnc-commercial-v5", false, true));
    }

    [Fact]
    public void IsAvailable_DevelopmentWithReceiptStore_AllowsLocalReviewWithoutProductionApproval()
    {
        Assert.True(CncQuotationAvailability.IsAvailable(true, false, null, true, false));
    }

    [Theory]
    [InlineData(false, "cnc-commercial-v5", true)]
    [InlineData(true, null, true)]
    [InlineData(true, "", true)]
    [InlineData(true, "cnc-commercial-v4", true)]
    [InlineData(true, "CNC-COMMERCIAL-V5", true)]
    [InlineData(true, " cnc-commercial-v5 ", true)]
    [InlineData(true, "cnc-commercial-v5", false)]
    public void IsAvailable_ProductionPrerequisiteMissingOrMismatched_Denies(
        bool isEnabled,
        string? approvedVersion,
        bool isSharedDistributedAtomic)
    {
        Assert.False(CncQuotationAvailability.IsAvailable(false, isEnabled, approvedVersion, true, isSharedDistributedAtomic));
    }

    [Fact]
    public void IsAvailable_ProductionWithExactApprovalAndAtomicReceiptStore_Allows()
    {
        Assert.True(CncQuotationAvailability.IsAvailable(false, true, "cnc-commercial-v5", true, true));
    }
}
