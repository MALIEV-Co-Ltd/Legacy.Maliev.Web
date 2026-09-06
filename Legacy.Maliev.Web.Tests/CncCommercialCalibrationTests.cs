namespace Legacy.Maliev.Web.Tests
{
    using Legacy.Maliev.Web.Application.Pricing;
    using System;
    using System.IO;
    using Xunit;

    public sealed class CncCommercialCalibrationTests
    {
        [Fact]
        public void PublishedRecovery_CoversAuditedBurdenAtTargetGrossMargin()
        {
            Assert.Equal(80M, CncCommercialCalibration.ProductiveMachineHoursPerMonth);
            Assert.InRange(CncCommercialCalibration.FullyBurdenedCostPerProductiveHour, 1387M, 1389M);
            Assert.InRange(CncCommercialCalibration.MinimumRecoveryBeforeVatPerMachineHour, 2313M, 2314M);
            Assert.Equal(2400M, CncCommercialCalibration.PublishedRecoveryBeforeVatPerMachineHour);
            Assert.True(CncCommercialCalibration.ExpectedGrossMarginRate >= CncCommercialCalibration.TargetGrossMarginRate);
        }

        [Fact]
        public void BrowserCoefficient_MatchesAuditedPublishedRecoveryWithoutExposingLedger()
        {
            string source = Read("Legacy.Maliev.Web", "wwwroot", "src", "app", "js", "cnc-quotation", "cnc-quotation-config.js");

            Assert.Contains("commercialRulesVersion: 'cnc-commercial-v6'", source, StringComparison.Ordinal);
            Assert.Contains("materialCertificateSupplierBeforeVat: 750", source, StringComparison.Ordinal);
            Assert.Contains(
                $"minimumContributionRecoveryBeforeVatPerMachineHour: {CncCommercialCalibration.PublishedRecoveryBeforeVatPerMachineHour:0}",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain("MonthlyMachineLoan", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MonthlyMachinistSalary", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FullyBurdenedCost", source, StringComparison.Ordinal);
        }

        private static string Read(params string[] pathSegments)
        {
            string relativePath = Path.Combine(pathSegments);
            for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
            }

            throw new FileNotFoundException($"Unable to find workspace file: {relativePath}");
        }
    }
}
