// <copyright file="FeatureMotionEstimator.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

/// <summary>Estimates bounded feature motion, non-deposition travel, cooling, and plate preparation.</summary>
public static class FeatureMotionEstimator
{
    /// <summary>Estimates one ordered plate's occupied-machine time without money.</summary>
    public static MotionLedger Estimate(IReadOnlyList<ExtrusionPath> paths, ResolvedSimulationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(profile);
        var roleSeconds = Enum.GetValues<ExtrusionRole>().ToDictionary(role => role, _ => 0d);
        if (paths.Count == 0)
        {
            return new MotionLedger(
                roleSeconds,
                0,
                0,
                PreparationSeconds(profile));
        }

        Validate(paths);
        double firstLayerZ = paths.SelectMany(path => path.Points).Min(point => point.Z);
        var ordered = paths
            .Select((path, index) => new { Path = path, Index = index, Z = path.Points.Min(point => point.Z) })
            .OrderBy(item => item.Z)
            .ThenBy(item => item.Index)
            .Select(item => item.Path)
            .ToArray();
        var layerPaths = new Dictionary<float, List<PathMotion>>();
        double travelSeconds = 0;
        double toolChangeSeconds = 0;
        Vector3? previousEnd = null;
        string previousMaterial = string.Empty;
        foreach (ExtrusionPath path in ordered)
        {
            float layer = path.Points[0].Z;
            double travelBeforePath = 0;
            if (previousEnd is Vector3 previous && Vector3.DistanceSquared(previous, path.Points[0]) > 1e-12)
            {
                travelBeforePath = TravelSeconds(previous, path.Points[0], profile);
                if (profile.Motion.RetractLengthMm > 0)
                {
                    travelBeforePath += 2 * profile.Motion.RetractLengthMm / profile.Motion.RetractSpeedMmPerSecond;
                }

                travelSeconds += travelBeforePath;
            }

            if (!string.IsNullOrEmpty(previousMaterial)
                && !string.Equals(previousMaterial, path.MaterialId, StringComparison.Ordinal))
            {
                toolChangeSeconds += profile.Motion.ToolChangeSeconds;
            }

            double speed = ResolveSpeed(path, profile, Math.Abs(layer - firstLayerZ) <= 0.0001f);
            double seconds = PathSeconds(path, speed, profile.Motion.PrintingAccelerationMmPerSecondSquared);
            if (!layerPaths.TryGetValue(layer, out List<PathMotion>? motions))
            {
                motions = [];
                layerPaths[layer] = motions;
            }

            motions.Add(new PathMotion(path, speed, seconds, travelBeforePath));
            previousEnd = path.Points[^1];
            previousMaterial = path.MaterialId;
        }

        double cooling = 0;
        foreach (List<PathMotion> layer in layerPaths.Values)
        {
            double travel = layer.Sum(item => item.TravelSeconds);
            double rawLayerSeconds = travel + layer.Sum(item => item.RawSeconds);
            double multiplier = ResolveCoolingMultiplier(layer, travel, rawLayerSeconds, profile);
            double slowedLayerSeconds = travel;
            foreach (PathMotion item in layer)
            {
                double speed = CoolingSpeed(item.SpeedMmPerSecond, multiplier, profile.Motion.MinimumCoolingSpeedMmPerSecond);
                double seconds = PathSeconds(item.Path, speed, profile.Motion.PrintingAccelerationMmPerSecondSquared);
                roleSeconds[item.Path.Role] += seconds;
                slowedLayerSeconds += seconds;
            }

            cooling += Math.Max(0, profile.Motion.MinimumLayerTimeSeconds - slowedLayerSeconds);
        }

        double preparation = PreparationSeconds(profile) + toolChangeSeconds;
        return new MotionLedger(roleSeconds, travelSeconds, cooling, preparation);
    }

    private static double PreparationSeconds(ResolvedSimulationProfile profile) =>
        profile.Motion.SetupSeconds
        + Math.Max(profile.Motion.NozzleHeatSeconds, profile.Motion.BedHeatSeconds)
        + profile.Motion.MachinePrepareCompensationSeconds
        + profile.Motion.MachineLoadFilamentSeconds;

    private static double ResolveCoolingMultiplier(
        IReadOnlyList<PathMotion> layer,
        double travelSeconds,
        double rawLayerSeconds,
        ResolvedSimulationProfile profile)
    {
        double target = profile.Motion.MinimumLayerTimeSeconds;
        if (target <= 0 || rawLayerSeconds >= target)
        {
            return 1;
        }

        double SlowLayer(double multiplier) => travelSeconds + layer.Sum(item => PathSeconds(
            item.Path,
            CoolingSpeed(item.SpeedMmPerSecond, multiplier, profile.Motion.MinimumCoolingSpeedMmPerSecond),
            profile.Motion.PrintingAccelerationMmPerSecondSquared));

        if (SlowLayer(0) < target)
        {
            return 0;
        }

        double slow = 0;
        double fast = 1;
        for (int iteration = 0; iteration < 40; iteration++)
        {
            double candidate = (slow + fast) / 2;
            if (SlowLayer(candidate) > target)
            {
                slow = candidate;
            }
            else
            {
                fast = candidate;
            }
        }

        return (slow + fast) / 2;
    }

    private static double CoolingSpeed(double nominalSpeed, double multiplier, double minimumCoolingSpeed)
    {
        if (minimumCoolingSpeed <= 0)
        {
            return nominalSpeed;
        }

        double slowed = nominalSpeed * multiplier;
        return Math.Min(nominalSpeed, Math.Max(minimumCoolingSpeed, slowed));
    }

    /// <summary>Calculates a bounded one-dimensional move with triangular/trapezoidal kinematics.</summary>
    public static double CalculateMoveSeconds(
        double lengthMm,
        double entrySpeedMmPerSecond,
        double exitSpeedMmPerSecond,
        double accelerationMmPerSecondSquared,
        double speedCapMmPerSecond)
    {
        if (!double.IsFinite(lengthMm) || lengthMm < 0
            || !double.IsFinite(entrySpeedMmPerSecond) || entrySpeedMmPerSecond < 0
            || !double.IsFinite(exitSpeedMmPerSecond) || exitSpeedMmPerSecond < 0
            || !double.IsFinite(accelerationMmPerSecondSquared) || accelerationMmPerSecondSquared <= 0
            || !double.IsFinite(speedCapMmPerSecond) || speedCapMmPerSecond <= 0
            || entrySpeedMmPerSecond > speedCapMmPerSecond
            || exitSpeedMmPerSecond > speedCapMmPerSecond)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthMm), "Motion values must be finite and physically bounded.");
        }

        if (lengthMm == 0)
        {
            return 0;
        }

        double accelerateDistance = ((speedCapMmPerSecond * speedCapMmPerSecond)
            - (entrySpeedMmPerSecond * entrySpeedMmPerSecond)) / (2 * accelerationMmPerSecondSquared);
        double decelerateDistance = ((speedCapMmPerSecond * speedCapMmPerSecond)
            - (exitSpeedMmPerSecond * exitSpeedMmPerSecond)) / (2 * accelerationMmPerSecondSquared);
        if (accelerateDistance + decelerateDistance <= lengthMm)
        {
            return ((speedCapMmPerSecond - entrySpeedMmPerSecond) / accelerationMmPerSecondSquared)
                + ((speedCapMmPerSecond - exitSpeedMmPerSecond) / accelerationMmPerSecondSquared)
                + ((lengthMm - accelerateDistance - decelerateDistance) / speedCapMmPerSecond);
        }

        double peakSquared = (accelerationMmPerSecondSquared * lengthMm)
            + (((entrySpeedMmPerSecond * entrySpeedMmPerSecond)
                + (exitSpeedMmPerSecond * exitSpeedMmPerSecond)) / 2);
        double peak = Math.Sqrt(Math.Max(peakSquared, 0));
        return ((peak - entrySpeedMmPerSecond) / accelerationMmPerSecondSquared)
            + ((peak - exitSpeedMmPerSecond) / accelerationMmPerSecondSquared);
    }

    private static double ResolveSpeed(ExtrusionPath path, ResolvedSimulationProfile profile, bool firstLayer)
    {
        if (!profile.SpeedMmPerSecond.TryGetValue(path.Role, out double roleSpeed))
        {
            throw new SimulationGeometryException("motion_role_speed_missing", $"No motion speed is resolved for role '{path.Role}'.");
        }

        double area = StadiumArea(path.WidthMm, path.HeightMm);
        double? flow = path.Role switch
        {
            ExtrusionRole.SupportInterface => profile.Support.InterfaceMaximumVolumetricFlowMm3PerSecond,
            ExtrusionRole.SupportBody or ExtrusionRole.SupportSheath => profile.Support.BodyMaximumVolumetricFlowMm3PerSecond,
            _ => profile.MaximumVolumetricFlowMm3PerSecond,
        };
        double cap = Math.Min(roleSpeed * path.SpeedMultiplier, profile.Motion.MaximumXYSpeedMmPerSecond);
        if (firstLayer)
        {
            cap *= profile.Motion.FirstLayerSpeedMultiplier;
        }

        if (flow is double volumetricFlow)
        {
            cap = Math.Min(cap, volumetricFlow / area);
        }

        if (!double.IsFinite(cap) || cap <= 0)
        {
            throw new SimulationGeometryException("motion_speed_invalid", "Resolved path speed is not physically valid.");
        }

        return cap;
    }

    private static double StadiumArea(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || height <= 0 || width < height)
        {
            throw new SimulationGeometryException("extrusion_geometry_invalid", "Extrusion width must be finite and at least its positive layer height.");
        }

        return (width * height) - (((4 - Math.PI) * height * height) / 4);
    }

    private static double PathSeconds(ExtrusionPath path, double speed, double acceleration)
    {
        double seconds = 0;
        double runLength = 0;
        Vector3? previousDirection = null;
        for (int index = 1; index < path.Points.Count; index++)
        {
            Vector3 delta = path.Points[index] - path.Points[index - 1];
            double length = delta.Length();
            if (length <= 1e-9)
            {
                continue;
            }

            Vector3 direction = Vector3.Normalize(delta);
            if (previousDirection is Vector3 prior && Vector3.Dot(prior, direction) < 0.999999)
            {
                seconds += CalculateMoveSeconds(runLength, 0, 0, acceleration, speed);
                runLength = 0;
            }

            runLength += length;
            previousDirection = direction;
        }

        return seconds + CalculateMoveSeconds(runLength, 0, 0, acceleration, speed);
    }

    private static double TravelSeconds(Vector3 start, Vector3 end, ResolvedSimulationProfile profile)
    {
        Vector2 xy = new(end.X - start.X, end.Y - start.Y);
        double xySeconds = CalculateMoveSeconds(
            xy.Length(),
            0,
            0,
            profile.Motion.TravelAccelerationMmPerSecondSquared,
            Math.Min(profile.Motion.TravelSpeedMmPerSecond, profile.Motion.MaximumXYSpeedMmPerSecond));
        double zSeconds = Math.Abs(end.Z - start.Z) / profile.Motion.MaximumZSpeedMmPerSecond;
        return Math.Max(xySeconds, zSeconds);
    }

    private static void Validate(IEnumerable<ExtrusionPath> paths)
    {
        foreach (ExtrusionPath path in paths)
        {
            if (path.Points.Count < 2 || string.IsNullOrWhiteSpace(path.MaterialId)
                || !double.IsFinite(path.WidthMm) || !double.IsFinite(path.HeightMm)
                || !double.IsFinite(path.SpeedMultiplier) || path.SpeedMultiplier <= 0
                || path.Points.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)))
            {
                throw new SimulationGeometryException("motion_path_invalid", "Motion paths require finite geometry, material, and modifiers.");
            }
        }
    }

    private sealed record PathMotion(
        ExtrusionPath Path,
        double SpeedMmPerSecond,
        double RawSeconds,
        double TravelSeconds);
}
