// <copyright file="ResinBuildProfileEvidence.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Operator-approved resin cycle and handling evidence. Nullable measurements are unknown,
    /// never zero; no generic printer or nominal vendor defaults are applied.
    /// </summary>
    internal sealed class ResinBuildProfileEvidence
    {
        internal string? ProfileVersion { get; set; }

        internal string? EvidenceClass { get; set; }

        internal string? SourceUri { get; set; }

        internal string? MachineId { get; set; }

        internal string? MaterialSku { get; set; }

        internal string? SlicerName { get; set; }

        internal string? SlicerVersion { get; set; }

        internal string? ProfileSha256 { get; set; }

        internal string? ApprovedBy { get; set; }

        internal DateTimeOffset? ApprovedAtUtc { get; set; }

        internal decimal? LayerHeightMm { get; set; }

        internal int? BottomLayerCount { get; set; }

        internal decimal? BottomExposureSeconds { get; set; }

        internal decimal? NormalExposureSeconds { get; set; }

        internal decimal? RestSecondsPerLayer { get; set; }

        internal decimal? LiftDistanceMm { get; set; }

        internal decimal? LiftSpeedMmPerSecond { get; set; }

        internal decimal? RetractDistanceMm { get; set; }

        internal decimal? RetractSpeedMmPerSecond { get; set; }

        internal decimal? UsablePlateWidthMm { get; set; }

        internal decimal? UsablePlateDepthMm { get; set; }

        internal decimal? PreparationSecondsPerBuild { get; set; }

        internal decimal? WashElapsedSeconds { get; set; }

        internal decimal? CureElapsedSeconds { get; set; }

        internal decimal? AttendedLaborSecondsPerBuild { get; set; }

        internal decimal? AttendedLaborSecondsPerPart { get; set; }

        internal decimal? ConsumablesThbPerBuild { get; set; }

        internal string? SupportStrategyVersion { get; set; }

        internal string? HollowingDrainPolicyVersion { get; set; }
    }

    /// <summary>Research-backed conservative resin evidence approved for preliminary pricing.</summary>
    internal static class ResinBuildProfileCatalog
    {
        /// <summary>
        /// Gets the generic 405 nm MSLA profile based on Phrozen's official Sonic Mighty 4K
        /// Aqua-Gray settings. This is deliberately provisional rather than represented as an
        /// operator-tuned production profile.
        /// </summary>
        internal static ResinBuildProfileEvidence ConservativeGeneric { get; } = new ResinBuildProfileEvidence
        {
            ProfileVersion = "generic-msla-mighty4k-aqua-gray-2026-09-19.v1",
            EvidenceClass = "research_provisional",
            SourceUri = "https://info.phrozen3d.com/pages/resin-sonic-mighty-4k",
            MachineId = "generic-405nm-msla-mighty4k-envelope",
            MaterialSku = "generic-405nm-standard-gray",
            SlicerName = "CHITUBOX-compatible CTB",
            SlicerVersion = "unversioned-vendor-profile",
            ProfileSha256 = "4CA2897D2D2A1DF72C17E959848979D33BAA74EB3310EF523F981F6950BCBBC7",
            ApprovedBy = "MALIEV owner research-profile decision",
            ApprovedAtUtc = DateTimeOffset.Parse("2026-09-19T00:00:00Z"),
            LayerHeightMm = 0.05m,
            BottomLayerCount = 6,
            BottomExposureSeconds = 32.5m,
            NormalExposureSeconds = 2.3m,
            RestSecondsPerLayer = 0m,
            LiftDistanceMm = 8m,
            LiftSpeedMmPerSecond = 1m,
            RetractDistanceMm = 8m,
            RetractSpeedMmPerSecond = 2.5m,
            UsablePlateWidthMm = 200m,
            UsablePlateDepthMm = 125m,
            PreparationSecondsPerBuild = 900m,
            WashElapsedSeconds = 120m,
            CureElapsedSeconds = 1800m,
            AttendedLaborSecondsPerBuild = 900m,
            AttendedLaborSecondsPerPart = 1800m,
            ConsumablesThbPerBuild = 100m,
            SupportStrategyVersion = "conservative-automatic-support-review-required.v1",
            HollowingDrainPolicyVersion = "solid-by-default-no-automatic-hollowing.v1",
        };
    }

    /// <summary>Fail-closed assessment of whether resin evidence can support an automated estimate.</summary>
    internal sealed class ResinProfileEvidenceAssessment
    {
        internal bool IsReady { get; set; }

        internal IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    }

    /// <summary>Validates resin evidence without supplying guessed values.</summary>
    internal static class ResinBuildProfileEvidenceValidator
    {
        internal static ResinProfileEvidenceAssessment Assess(ResinBuildProfileEvidence? profile)
        {
            var reasons = new List<string>();
            if (profile == null)
            {
                reasons.Add("resin_profile_missing");
                return Blocked(reasons);
            }

            RequireText(profile.ProfileVersion, "profile_version_missing", reasons);
            RequireText(profile.MachineId, "machine_id_missing", reasons);
            RequireText(profile.MaterialSku, "material_sku_missing", reasons);
            RequireText(profile.SlicerName, "slicer_name_missing", reasons);
            RequireText(profile.SlicerVersion, "slicer_version_missing", reasons);
            if (string.IsNullOrWhiteSpace(profile.ProfileSha256)
                || profile.ProfileSha256.Length != 64
                || !IsHex(profile.ProfileSha256))
            {
                reasons.Add("profile_digest_missing_or_invalid");
            }

            RequireText(profile.ApprovedBy, "operator_approval_missing", reasons);
            if (!profile.ApprovedAtUtc.HasValue)
            {
                reasons.Add("approval_timestamp_missing");
            }

            RequirePositive(profile.LayerHeightMm, "layer_height_missing", reasons);
            RequireNonNegative(profile.BottomLayerCount, "bottom_layer_count_missing", reasons);
            RequirePositive(profile.BottomExposureSeconds, "bottom_exposure_missing", reasons);
            RequirePositive(profile.NormalExposureSeconds, "normal_exposure_missing", reasons);
            RequireNonNegative(profile.RestSecondsPerLayer, "rest_time_missing", reasons);
            RequirePositive(profile.LiftDistanceMm, "lift_distance_missing", reasons);
            RequirePositive(profile.LiftSpeedMmPerSecond, "lift_speed_missing", reasons);
            RequirePositive(profile.RetractDistanceMm, "retract_distance_missing", reasons);
            RequirePositive(profile.RetractSpeedMmPerSecond, "retract_speed_missing", reasons);
            RequirePositive(profile.UsablePlateWidthMm, "plate_width_missing", reasons);
            RequirePositive(profile.UsablePlateDepthMm, "plate_depth_missing", reasons);
            RequireNonNegative(profile.PreparationSecondsPerBuild, "preparation_time_missing", reasons);
            RequireNonNegative(profile.WashElapsedSeconds, "wash_time_missing", reasons);
            RequireNonNegative(profile.CureElapsedSeconds, "cure_time_missing", reasons);
            RequireNonNegative(profile.AttendedLaborSecondsPerBuild, "build_labor_missing", reasons);
            RequireNonNegative(profile.AttendedLaborSecondsPerPart, "part_labor_missing", reasons);
            RequireNonNegative(profile.ConsumablesThbPerBuild, "consumables_missing", reasons);
            RequireText(profile.SupportStrategyVersion, "support_strategy_missing", reasons);
            RequireText(profile.HollowingDrainPolicyVersion, "hollowing_drain_policy_missing", reasons);

            return reasons.Count == 0
                ? new ResinProfileEvidenceAssessment { IsReady = true }
                : Blocked(reasons);
        }

        private static ResinProfileEvidenceAssessment Blocked(IReadOnlyList<string> reasons)
        {
            return new ResinProfileEvidenceAssessment
            {
                IsReady = false,
                ReasonCodes = reasons,
            };
        }

        private static void RequireText(string? value, string reason, ICollection<string> reasons)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                reasons.Add(reason);
            }
        }

        private static void RequirePositive(decimal? value, string reason, ICollection<string> reasons)
        {
            if (!value.HasValue || value.Value <= 0m)
            {
                reasons.Add(reason);
            }
        }

        private static void RequireNonNegative(decimal? value, string reason, ICollection<string> reasons)
        {
            if (!value.HasValue || value.Value < 0m)
            {
                reasons.Add(reason);
            }
        }

        private static void RequireNonNegative(int? value, string reason, ICollection<string> reasons)
        {
            if (!value.HasValue || value.Value < 0)
            {
                reasons.Add(reason);
            }
        }

        private static bool IsHex(string value)
        {
            foreach (char character in value)
            {
                bool digit = character >= '0' && character <= '9';
                bool lower = character >= 'a' && character <= 'f';
                bool upper = character >= 'A' && character <= 'F';
                if (!digit && !lower && !upper)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
