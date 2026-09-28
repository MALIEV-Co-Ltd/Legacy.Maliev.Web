// <copyright file="FdmSimulationEngine.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

/// <summary>Runs the deterministic fixed-pose physical simulation pipeline.</summary>
public static class FdmSimulationEngine
{
    /// <summary>Gets the current physical-analysis algorithm revision.</summary>
    public const string AnalysisVersion = "fdm-physical-v2";

    /// <summary>Estimates deposited roles and occupied-machine time without commercial money.</summary>
    public static SimulationResult Estimate(SimulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Quantity <= 0)
        {
            throw new SimulationGeometryException("quantity_invalid", "Simulation quantity must be positive.");
        }

        IReadOnlyList<LayerContours> layers = LayerContourBuilder.Build(
            request.Mesh,
            request.Pose,
            request.Profile,
            request.Budget);
        ModelPathPlan model = ModelPathPlanner.Build(layers, request.Profile);
        SupportPlan support = request.Profile.Support.Mode == "tree"
            ? TreeSupportPlanner.Build(request.Mesh, layers, model, request.Profile)
            : SupportPlanner.Build(layers, model, request.Profile);
        ExtrusionPath[] paths = model.Paths.Concat(support.Paths).ToArray();
        int segments = paths.Sum(path => Math.Max(0, path.Points.Count - 1));
        if (segments > request.Budget.MaximumPathSegments)
        {
            throw new SimulationGeometryException("path_segment_budget_exceeded", "Planned model and support paths exceed the analysis budget.");
        }

        MotionLedger singleMotion = FeatureMotionEstimator.Estimate(paths, request.Profile);
        MotionLedger motion = ScaleMotion(singleMotion, request.Quantity);
        RoleDeposition[] roles = BuildRoleLedger(paths, singleMotion, request.Quantity);
        string[] diagnostics = request.Mesh.Diagnostics
            .Concat(model.Diagnostics)
            .Concat(support.Diagnostics)
            .Concat(support.UnsatisfiedDemand.Count == 0 ? [] : ["support_unsatisfied_demand"])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return SimulationResult.Create(
            AnalysisVersion,
            request.Profile.ResolvedProfileSha256,
            roles,
            motion,
            support.Regions,
            diagnostics);
    }

    private static RoleDeposition[] BuildRoleLedger(
        IReadOnlyList<ExtrusionPath> paths,
        MotionLedger motion,
        int quantity)
    {
        return paths
            .GroupBy(path => new { path.Role, path.MaterialId })
            .Select(group =>
            {
                double groupVolume = group.Sum(DepositedVolume);
                double roleVolume = paths.Where(path => path.Role == group.Key.Role).Sum(DepositedVolume);
                double roleSeconds = motion.RoleSeconds.GetValueOrDefault(group.Key.Role);
                double allocatedSeconds = roleVolume <= 0 ? 0 : roleSeconds * groupVolume / roleVolume;
                return new RoleDeposition(
                    group.Key.Role,
                    group.Key.MaterialId,
                    groupVolume * quantity,
                    allocatedSeconds * quantity);
            })
            .OrderBy(row => row.Role)
            .ThenBy(row => row.MaterialId, StringComparer.Ordinal)
            .ToArray();
    }

    private static MotionLedger ScaleMotion(MotionLedger motion, int quantity) => new(
        motion.RoleSeconds.ToDictionary(pair => pair.Key, pair => pair.Value * quantity),
        motion.TravelSeconds * quantity,
        motion.CoolingSeconds * quantity,
        motion.PreparationSeconds);

    private static double DepositedVolume(ExtrusionPath path)
    {
        double area = (path.WidthMm * path.HeightMm)
            - (((4 - Math.PI) * path.HeightMm * path.HeightMm) / 4);
        double length = 0;
        for (int index = 1; index < path.Points.Count; index++)
        {
            length += Vector3.Distance(path.Points[index - 1], path.Points[index]);
        }

        return area * length;
    }
}
