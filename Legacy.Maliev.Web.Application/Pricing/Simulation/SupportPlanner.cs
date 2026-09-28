// <copyright file="SupportPlanner.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using Clipper2Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

/// <summary>Builds connected normal-support columns with separate body/interface ownership.</summary>
public static class SupportPlanner
{
    /// <summary>Plans normal supports from unresolved model support demand.</summary>
    public static SupportPlan Build(
        IReadOnlyList<LayerContours> layers,
        ModelPathPlan model,
        ResolvedSimulationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(profile);
        if (!string.Equals(profile.Support.Mode, "normal", StringComparison.Ordinal))
        {
            throw new SimulationGeometryException("support_mode_mismatch", "Normal support planner requires a normal support profile.");
        }

        if (model.SupportDemandMasks.Count == 0)
        {
            return new SupportPlan([], [], [], [], []);
        }

        if (!profile.Support.Enabled)
        {
            return new SupportPlan(
                [],
                [],
                model.SupportDemandMasks.ToArray(),
                [],
                ["support_disabled_with_demand"]);
        }

        PathsD[] occupancy = Enumerable.Range(0, layers.Count).Select(_ => new PathsD()).ToArray();
        PathsD[] interfaces = Enumerable.Range(0, layers.Count).Select(_ => new PathsD()).ToArray();
        var unsatisfied = new List<LayerMask>();
        var diagnostics = new SortedSet<string>(StringComparer.Ordinal);
        foreach (LayerMask demand in model.SupportDemandMasks)
        {
            int demandIndex = FindLayer(layers, demand.ZMm);
            int gapLayers = Math.Max(1, (int)Math.Ceiling(profile.Support.TopZGapMm / profile.LayerHeightMm));
            int contactIndex = demandIndex - gapLayers;
            if (contactIndex < 0)
            {
                unsatisfied.Add(demand);
                diagnostics.Add("support_z_gap_unreachable");
                continue;
            }

            PathsD candidate = PolygonMath.Offset(PolygonMath.ToPaths(demand.Rings), profile.Support.ExpansionMm);
            var column = new Dictionary<int, PathsD>();
            bool connected = true;
            bool modelTerminated = false;
            for (int layerIndex = contactIndex; layerIndex >= 0; layerIndex--)
            {
                PathsD collision = PolygonMath.Offset(
                    PolygonMath.ToPaths(layers[layerIndex].Rings),
                    profile.Support.XYClearanceMm);
                PathsD available = collision.Count == 0
                    ? candidate
                    : Clipper.Difference(candidate, collision, FillRule.NonZero, 3);
                if (available.Count == 0)
                {
                    if (!profile.Support.BuildPlateOnly && layerIndex < contactIndex)
                    {
                        modelTerminated = true;
                        diagnostics.Add("support_model_terminated");
                        break;
                    }

                    connected = false;
                    break;
                }

                column[layerIndex] = available;
                candidate = profile.Support.ExpansionMm == 0
                    ? available
                    : PolygonMath.Offset(available, profile.Support.ExpansionMm);
            }

            if (!connected || (column.Count == 0) || (!modelTerminated && !column.ContainsKey(0)))
            {
                unsatisfied.Add(demand);
                diagnostics.Add("support_accessibility_unresolved");
                continue;
            }

            foreach ((int layerIndex, PathsD region) in column)
            {
                occupancy[layerIndex] = PolygonMath.Union(occupancy[layerIndex], region);
                if (contactIndex - layerIndex < profile.Support.InterfaceLayers)
                {
                    interfaces[layerIndex] = PolygonMath.Union(interfaces[layerIndex], region);
                }
            }
        }

        var paths = new List<ExtrusionPath>();
        var overlay = new List<LayerMask>();
        double envelopeMm3 = 0;
        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            if (occupancy[layerIndex].Count == 0)
            {
                continue;
            }

            LayerContours layer = layers[layerIndex];
            PathsD interfaceRegion = Clipper.Intersect(occupancy[layerIndex], interfaces[layerIndex], FillRule.NonZero, 3);
            PathsD bodyRegion = Clipper.Difference(occupancy[layerIndex], interfaceRegion, FillRule.NonZero, 3);
            envelopeMm3 += PolygonMath.NetArea(PolygonMath.ToRings(occupancy[layerIndex])) * layer.ThicknessMm;
            overlay.Add(new LayerMask(layer.ZMm, PolygonMath.ToRings(occupancy[layerIndex])));
            paths.AddRange(InfillPathPlanner.Build(
                PolygonMath.ToRings(bodyRegion),
                layer.ZMm,
                layer.ThicknessMm,
                profile.Support.BodyLineWidthMm,
                profile.Support.BodyDensity,
                "rectilinear",
                layerIndex,
                profile.Support.BodyMaterialId,
                ExtrusionRole.SupportBody));
            paths.AddRange(InfillPathPlanner.Build(
                PolygonMath.ToRings(interfaceRegion),
                layer.ZMm,
                layer.ThicknessMm,
                profile.Support.InterfaceLineWidthMm,
                profile.Support.InterfaceDensity,
                "rectilinear",
                layerIndex,
                profile.Support.InterfaceMaterialId,
                ExtrusionRole.SupportInterface));
        }

        double bodyVolume = DepositedVolume(paths, ExtrusionRole.SupportBody);
        double interfaceVolume = DepositedVolume(paths, ExtrusionRole.SupportInterface);
        IReadOnlyList<SupportRegionReport> regions = paths.Count == 0
            ? []
            :
            [
                new SupportRegionReport(
                    "support-0001",
                    envelopeMm3,
                    bodyVolume,
                    interfaceVolume,
                    0,
                    diagnostics.ToArray()),
            ];
        return new SupportPlan(paths, regions, unsatisfied, overlay, diagnostics.ToArray());
    }

    /// <summary>Converts independently deposited body/interface volumes and densities to grams.</summary>
    public static double CalculateMassGrams(
        double bodyDepositedMm3,
        double bodyDensityGramsPerCm3,
        double interfaceDepositedMm3,
        double interfaceDensityGramsPerCm3)
    {
        if (bodyDepositedMm3 < 0 || interfaceDepositedMm3 < 0
            || bodyDensityGramsPerCm3 <= 0 || interfaceDensityGramsPerCm3 <= 0
            || !double.IsFinite(bodyDepositedMm3) || !double.IsFinite(interfaceDepositedMm3)
            || !double.IsFinite(bodyDensityGramsPerCm3) || !double.IsFinite(interfaceDensityGramsPerCm3))
        {
            throw new ArgumentOutOfRangeException(nameof(bodyDepositedMm3), "Support volumes and densities must be finite and physically valid.");
        }

        return ((bodyDepositedMm3 / 1000) * bodyDensityGramsPerCm3)
            + ((interfaceDepositedMm3 / 1000) * interfaceDensityGramsPerCm3);
    }

    private static int FindLayer(IReadOnlyList<LayerContours> layers, double zMm)
    {
        int result = -1;
        double best = double.MaxValue;
        for (int index = 0; index < layers.Count; index++)
        {
            double distance = Math.Abs(layers[index].ZMm - zMm);
            if (distance < best)
            {
                best = distance;
                result = index;
            }
        }

        if (result < 0 || best > 0.001)
        {
            throw new SimulationGeometryException("support_layer_mismatch", "Support demand does not map to a physical layer.");
        }

        return result;
    }

    private static double DepositedVolume(IEnumerable<ExtrusionPath> paths, ExtrusionRole role) => paths
        .Where(path => path.Role == role)
        .Sum(path => PathLength(path) * path.WidthMm * path.HeightMm);

    private static double PathLength(ExtrusionPath path)
    {
        double length = 0;
        for (int index = 1; index < path.Points.Count; index++)
        {
            length += Vector3.Distance(path.Points[index - 1], path.Points[index]);
        }

        return length;
    }
}
