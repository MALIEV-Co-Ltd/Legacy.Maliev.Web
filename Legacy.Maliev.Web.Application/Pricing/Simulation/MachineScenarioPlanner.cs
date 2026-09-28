// <copyright file="MachineScenarioPlanner.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

/// <summary>Evaluates orientation fit, usable-bed packing, and reviewed partition alternatives.</summary>
public static class MachineScenarioPlanner
{
    /// <summary>Evaluates a deterministic machine scenario without resizing customer geometry.</summary>
    public static MachineScenario Evaluate(
        NormalizedMesh mesh,
        ResolvedSimulationProfile profile,
        int quantity,
        AnalysisBudget budget,
        Pose? fixedPose = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(budget);
        if (quantity <= 0)
        {
            throw new SimulationGeometryException("quantity_invalid", "Scenario quantity must be positive.");
        }

        double margin = profile.BrimWidthMm + profile.Support.ExpansionMm;
        if (profile.Support.Mode == "tree")
        {
            margin += profile.Support.Tree.MaximumRadiusMm;
        }

        var coarse = new List<(Pose Pose, Dimensions Dimensions, List<string> Reasons, int Capacity)>();
        foreach (Pose pose in OrientationPlanner.GenerateCandidates(mesh, fixedPose))
        {
            Dimensions dimensions = GetDimensions(mesh, pose, margin);
            var reasons = new List<string>();
            if (dimensions.Width > profile.PrintableWidthMm || dimensions.Depth > profile.PrintableDepthMm)
            {
                reasons.Add("machine_xy_envelope_exceeded");
            }

            if (dimensions.Height > profile.PrintableHeightMm)
            {
                reasons.Add("machine_height_exceeded");
            }

            int capacity = reasons.Count == 0 ? PlateCapacity(dimensions, profile) : 0;
            if (capacity == 0 && reasons.Count == 0)
            {
                reasons.Add(profile.ExcludedZones.Count > 0
                    ? "machine_excluded_zone_collision"
                    : "machine_xy_envelope_exceeded");
            }

            coarse.Add((pose, dimensions, reasons, capacity));
        }

        var refinedIds = coarse
            .Where(item => item.Reasons.Count == 0)
            .OrderBy(item => item.Dimensions.Height)
            .ThenBy(item => item.Dimensions.Width * item.Dimensions.Depth)
            .ThenBy(item => item.Pose.Id, StringComparer.Ordinal)
            .Take(4)
            .Select(item => item.Pose.Id)
            .ToHashSet(StringComparer.Ordinal);
        var evaluations = new List<OrientationEvaluation>();
        foreach ((Pose pose, Dimensions dimensions, List<string> reasons, _) in coarse)
        {
            SimulationResult? result = null;
            if (reasons.Count == 0 && refinedIds.Contains(pose.Id))
            {
                try
                {
                    result = FdmSimulationEngine.Estimate(new SimulationRequest(mesh, pose, profile, 1, budget));
                }
                catch (SimulationGeometryException exception)
                {
                    reasons.Add(exception.ReasonCode);
                }
            }
            else if (reasons.Count == 0)
            {
                reasons.Add("coarse_candidate_not_refined");
            }

            evaluations.Add(new OrientationEvaluation(
                pose,
                reasons.Count == 0,
                reasons.ToArray(),
                dimensions.Width,
                dimensions.Depth,
                dimensions.Height,
                result));
        }

        OrientationEvaluation? selected = evaluations
            .Where(item => item.Feasible && item.Result is not null)
            .OrderBy(item => item.Result!.Diagnostics.Count)
            .ThenBy(item => item.Result!.SupportDepositedMm3)
            .ThenBy(item => item.Result!.Motion.TotalSeconds)
            .ThenBy(item => item.HeightMm)
            .ThenBy(item => item.Pose.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (selected is null)
        {
            PartitionProposal partition = CreatePartition(mesh, profile, margin);
            return new MachineScenario(
                profile.MachineId,
                null,
                evaluations,
                [],
                partition,
                ["requires_machine_or_partition"]);
        }

        int capacityPerPlate = coarse.Single(item => item.Pose.Id == selected.Pose.Id).Capacity;
        var plates = new List<PlateAssignment>();
        int remaining = quantity;
        int plateNumber = 1;
        while (remaining > 0)
        {
            int onPlate = Math.Min(capacityPerPlate, remaining);
            SimulationResult result = FdmSimulationEngine.Estimate(new SimulationRequest(
                mesh,
                selected.Pose,
                profile,
                onPlate,
                budget));
            plates.Add(new PlateAssignment(plateNumber++, onPlate, result));
            remaining -= onPlate;
        }

        return new MachineScenario(profile.MachineId, selected.Pose, evaluations, plates, null, []);
    }

    private static int PlateCapacity(Dimensions part, ResolvedSimulationProfile profile)
    {
        int columns = (int)Math.Floor(profile.PrintableWidthMm / part.Width);
        int rows = (int)Math.Floor(profile.PrintableDepthMm / part.Depth);
        int capacity = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                double x1 = column * part.Width;
                double y1 = row * part.Depth;
                double x2 = x1 + part.Width;
                double y2 = y1 + part.Depth;
                if (!profile.ExcludedZones.Any(zone => RectanglesOverlap(
                    x1,
                    y1,
                    x2,
                    y2,
                    zone.MinimumX,
                    zone.MinimumY,
                    zone.MaximumX,
                    zone.MaximumY)))
                {
                    capacity++;
                }
            }
        }

        return capacity;
    }

    private static bool RectanglesOverlap(
        double firstX1,
        double firstY1,
        double firstX2,
        double firstY2,
        double secondX1,
        double secondY1,
        double secondX2,
        double secondY2) => firstX1 < secondX2 && firstX2 > secondX1 && firstY1 < secondY2 && firstY2 > secondY1;

    private static Dimensions GetDimensions(NormalizedMesh mesh, Pose pose, double margin)
    {
        Vector3[] points = mesh.Triangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Select(point => Vector3.Transform(point, pose.Transform))
            .ToArray();
        double width = points.Max(point => point.X) - points.Min(point => point.X) + (2 * margin);
        double depth = points.Max(point => point.Y) - points.Min(point => point.Y) + (2 * margin);
        double height = points.Max(point => point.Z) - points.Min(point => point.Z);
        return new Dimensions(width, depth, height);
    }

    private static PartitionProposal CreatePartition(
        NormalizedMesh mesh,
        ResolvedSimulationProfile profile,
        double margin)
    {
        Dimensions source = GetDimensions(mesh, new Pose("source", Matrix4x4.Identity), 0);
        (string axis, double length, double limit) = source.Width >= source.Depth && source.Width >= source.Height
            ? ("X", source.Width, profile.PrintableWidthMm - (2 * margin))
            : source.Depth >= source.Height
                ? ("Y", source.Depth, profile.PrintableDepthMm - (2 * margin))
                : ("Z", source.Height, profile.PrintableHeightMm);
        var cuts = new List<double>();
        if (limit > 0)
        {
            for (double cut = limit; cut < length; cut += limit)
            {
                cuts.Add(cut);
            }
        }

        return new PartitionProposal(
            axis,
            cuts,
            ["new_cut_skins_required", "joint_allowance_not_validated", "assembly_review_required"]);
    }

    private sealed record Dimensions(double Width, double Depth, double Height);
}
