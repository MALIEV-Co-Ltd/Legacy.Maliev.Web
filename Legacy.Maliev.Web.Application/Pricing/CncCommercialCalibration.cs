namespace Legacy.Maliev.Web.Application.Pricing
{
    /// <summary>
    /// Internal audit model for the public CNC contribution-recovery coefficient.
    /// </summary>
    /// <remarks>
    /// The customer calculator receives only <see cref="PublishedRecoveryBeforeVatPerMachineHour"/>.
    /// The cost ledger remains server-side and exists to prevent a public coefficient from being
    /// reduced below the audited profitability threshold.
    /// </remarks>
    internal static class CncCommercialCalibration
    {
        internal const string CommercialRulesVersion = "cnc-commercial-v5";
        internal const decimal ScheduledMachineHoursPerMonth = 200M;
        internal const decimal UtilizationRate = 0.40M;
        internal const decimal ProductiveMachineHoursPerMonth = ScheduledMachineHoursPerMonth * UtilizationRate;

        internal const decimal WorkbookMachineHourlyCost = 378M;
        internal const decimal WorkbookDepreciationPerMachineHour = 56.25M;
        internal const decimal MonthlyMachineLoan = 40000M;
        internal const decimal MonthlyMachinistSalary = 25000M;
        internal const decimal MonthlyFusionLicenses = 3067.333333M;
        internal const decimal MonthlyCoolant = 3375M;
        internal const decimal MonthlyCompressorMaintenance = 3750M;

        internal const decimal SharedOverheadReserveRate = 0.10M;
        internal const decimal TargetGrossMarginRate = 0.40M;
        internal const decimal PublishedRecoveryBeforeVatPerMachineHour = 2400M;

        internal static decimal CashMachineCostPerProductiveHour =>
            WorkbookMachineHourlyCost
            - WorkbookDepreciationPerMachineHour
            + (MonthlyMachineLoan / ProductiveMachineHoursPerMonth);

        internal static decimal DedicatedOperatingCostPerProductiveHour =>
            CashMachineCostPerProductiveHour
            + (MonthlyMachinistSalary / ProductiveMachineHoursPerMonth)
            + (MonthlyFusionLicenses / ProductiveMachineHoursPerMonth)
            + (MonthlyCoolant / ProductiveMachineHoursPerMonth)
            + (MonthlyCompressorMaintenance / ProductiveMachineHoursPerMonth);

        internal static decimal FullyBurdenedCostPerProductiveHour =>
            DedicatedOperatingCostPerProductiveHour * (1M + SharedOverheadReserveRate);

        internal static decimal MinimumRecoveryBeforeVatPerMachineHour =>
            FullyBurdenedCostPerProductiveHour / (1M - TargetGrossMarginRate);

        internal static decimal ExpectedGrossMarginRate =>
            1M - (FullyBurdenedCostPerProductiveHour / PublishedRecoveryBeforeVatPerMachineHour);
    }
}
