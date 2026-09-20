// <copyright file="ResinBuildProfileEvidenceTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests
{
    using Legacy.Maliev.Web.Application.Pricing;
    using System;
    using Xunit;

    /// <summary>Verifies the provisional resin evidence and its fail-closed validation boundary.</summary>
    public sealed class ResinBuildProfileEvidenceTests
    {
        [Fact]
        public void GenericResearchProfile_UsesConservativeOfficialMightyFourKCycle()
        {
            ResinBuildProfileEvidence profile = ResinBuildProfileCatalog.ConservativeGeneric;
            ResinProfileEvidenceAssessment result = ResinBuildProfileEvidenceValidator.Assess(profile);

            Assert.True(result.IsReady);
            Assert.Equal("research_provisional", profile.EvidenceClass);
            Assert.Equal("https://info.phrozen3d.com/pages/resin-sonic-mighty-4k", profile.SourceUri);
            Assert.Equal(0.05m, profile.LayerHeightMm);
            Assert.Equal(6, profile.BottomLayerCount);
            Assert.Equal(32.5m, profile.BottomExposureSeconds);
            Assert.Equal(2.3m, profile.NormalExposureSeconds);
            Assert.Equal(8m, profile.LiftDistanceMm);
            decimal normalCycle = profile.NormalExposureSeconds.GetValueOrDefault()
                + (profile.LiftDistanceMm.GetValueOrDefault() / profile.LiftSpeedMmPerSecond.GetValueOrDefault())
                + (profile.RetractDistanceMm.GetValueOrDefault() / profile.RetractSpeedMmPerSecond.GetValueOrDefault());
            Assert.Equal(Convert.ToDecimal(PricingCatalog.ResinPerLayerSeconds), normalCycle);
            Assert.Equal(
                Convert.ToDecimal(PricingCatalog.ResinBottomLayerExtraSeconds),
                profile.BottomExposureSeconds.GetValueOrDefault() - profile.NormalExposureSeconds.GetValueOrDefault());
        }

        [Fact]
        public void ResinMinutes_IncludesOfficialLiftRetractAndBottomExposureCycle()
        {
            double minutes = PrintTimeCalculator.ResinMinutes(new GeometryInput { HeightMm = 10 });

            Assert.Equal(48.02, minutes, 2);
        }

        /// <summary>A missing profile is unavailable, not a generic 2.5-second cycle.</summary>
        [Fact]
        public void Assess_MissingProfile_IsExplicitlyBlocked()
        {
            ResinProfileEvidenceAssessment result = ResinBuildProfileEvidenceValidator.Assess(null);

            Assert.False(result.IsReady);
            Assert.Contains("resin_profile_missing", result.ReasonCodes);
        }

        /// <summary>Workbook machine labels do not satisfy operator/profile approval.</summary>
        [Fact]
        public void Assess_WorkbookLabelOnly_DoesNotBecomeAnAutomatedProfile()
        {
            ResinProfileEvidenceAssessment result = ResinBuildProfileEvidenceValidator.Assess(
                new ResinBuildProfileEvidence
                {
                    MachineId = "MEGA8K",
                    MaterialSku = "M68",
                });

            Assert.False(result.IsReady);
            Assert.Contains("operator_approval_missing", result.ReasonCodes);
            Assert.Contains("normal_exposure_missing", result.ReasonCodes);
            Assert.Contains("wash_time_missing", result.ReasonCodes);
            Assert.Contains("profile_digest_missing_or_invalid", result.ReasonCodes);
        }

        /// <summary>A complete synthetic contract can be recognized without defining production constants.</summary>
        [Fact]
        public void Assess_CompleteSyntheticEvidence_IsReady()
        {
            ResinProfileEvidenceAssessment result = ResinBuildProfileEvidenceValidator.Assess(
                new ResinBuildProfileEvidence
                {
                    ProfileVersion = "synthetic-test-only",
                    MachineId = "synthetic-machine",
                    MaterialSku = "synthetic-material",
                    SlicerName = "synthetic-slicer",
                    SlicerVersion = "1.0",
                    ProfileSha256 = new string('A', 64),
                    ApprovedBy = "test-operator",
                    ApprovedAtUtc = DateTimeOffset.Parse("2026-09-19T00:00:00Z"),
                    LayerHeightMm = 0.05m,
                    BottomLayerCount = 5,
                    BottomExposureSeconds = 20m,
                    NormalExposureSeconds = 2m,
                    RestSecondsPerLayer = 1m,
                    LiftDistanceMm = 5m,
                    LiftSpeedMmPerSecond = 1m,
                    RetractDistanceMm = 5m,
                    RetractSpeedMmPerSecond = 2m,
                    UsablePlateWidthMm = 100m,
                    UsablePlateDepthMm = 100m,
                    PreparationSecondsPerBuild = 60m,
                    WashElapsedSeconds = 300m,
                    CureElapsedSeconds = 300m,
                    AttendedLaborSecondsPerBuild = 60m,
                    AttendedLaborSecondsPerPart = 30m,
                    ConsumablesThbPerBuild = 10m,
                    SupportStrategyVersion = "synthetic-support",
                    HollowingDrainPolicyVersion = "no-automatic-hollowing",
                });

            Assert.True(result.IsReady);
            Assert.Empty(result.ReasonCodes);
        }
    }
}
