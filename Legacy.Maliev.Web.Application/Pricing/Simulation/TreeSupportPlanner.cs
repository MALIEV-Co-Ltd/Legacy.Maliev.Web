// <copyright file="TreeSupportPlanner.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using Clipper2Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

/// <summary>Builds deterministic collision-checked tree support graphs.</summary>
public static class TreeSupportPlanner
{
    /// <summary>Builds provisional tree paths while retaining unreachable demand.</summary>
    public static SupportPlan Build(
        NormalizedMesh mesh,
        IReadOnlyList<LayerContours> layers,
        ModelPathPlan model,
        ResolvedSimulationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(profile);
        if (!string.Equals(profile.Support.Mode, "tree", StringComparison.Ordinal))
        {
            throw new SimulationGeometryException("support_mode_mismatch", "Tree support planner requires a tree support profile.");
        }

        var diagnostics = new SortedSet<string>(StringComparer.Ordinal) { "tree_support_provisional" };
        if (model.SupportDemandMasks.Count == 0)
        {
            return new SupportPlan([], [], [], [], diagnostics.ToArray());
        }

        if (!profile.Support.Enabled)
        {
            diagnostics.Add("support_disabled_with_demand");
            return new SupportPlan([], [], model.SupportDemandMasks.ToArray(), [], diagnostics.ToArray());
        }

        List<Tip> tips = CreateTips(model.SupportDemandMasks, layers, profile);
        var active = new List<Node>();
        var segments = new List<BranchSegment>();
        var invalidTipIds = new HashSet<int>();
        int highestLayer = tips.Max(tip => tip.ContactLayer);
        double maximumStep = profile.LayerHeightMm
            * Math.Tan(profile.Support.Tree.MaximumBranchAngleFromVerticalDegrees * Math.PI / 180);
        for (int layerIndex = highestLayer; layerIndex >= 0; layerIndex--)
        {
            active.AddRange(tips
                .Where(tip => tip.ContactLayer == layerIndex)
                .Select(tip => new Node(tip.Position, [tip.Id])));
            if (active.Count == 0)
            {
                continue;
            }

            Vector2 centroid = new(
                active.Average(node => node.Position.X),
                active.Average(node => node.Position.Y));
            var descended = new List<Node>();
            foreach (Node node in active.OrderBy(node => node.Position.X).ThenBy(node => node.Position.Y))
            {
                Vector2 direction = centroid - node.Position;
                Vector2 next = direction.Length() <= maximumStep || direction == Vector2.Zero
                    ? centroid
                    : node.Position + (Vector2.Normalize(direction) * (float)maximumStep);
                if (Collides(next, layers[layerIndex].Rings, profile.Support.XYClearanceMm))
                {
                    foreach (int tipId in node.TipIds)
                    {
                        invalidTipIds.Add(tipId);
                    }

                    diagnostics.Add("tree_tip_unreachable");
                    continue;
                }

                float upperZ = (float)(layers[layerIndex].ZMm + profile.LayerHeightMm);
                float lowerZ = (float)layers[layerIndex].ZMm;
                segments.Add(new BranchSegment(node.Position, next, upperZ, lowerZ, node.TipIds));
                descended.Add(new Node(next, node.TipIds));
            }

            active = Merge(descended, maximumStep + profile.Support.Tree.MinimumRadiusMm);
        }

        if (active.Count > 1)
        {
            diagnostics.Add("tree_branch_angle_limited");
        }

        var validTipIds = tips.Select(tip => tip.Id).Where(id => !invalidTipIds.Contains(id)).ToHashSet();
        var paths = new List<ExtrusionPath>();
        double envelopeMm3 = 0;
        foreach (BranchSegment segment in segments.Where(segment => segment.TipIds.All(validTipIds.Contains)))
        {
            int carriedTips = segment.TipIds.Count;
            double radius = Math.Min(
                profile.Support.Tree.MaximumRadiusMm,
                profile.Support.Tree.MinimumRadiusMm + ((carriedTips - 1) * profile.Support.Tree.RadiusPerTipMm));
            double solidArea = Math.PI * radius * radius;
            double wallThickness = profile.Support.Tree.WallCount * profile.Support.BodyLineWidthMm;
            double innerRadius = Math.Max(0, radius - wallThickness);
            double depositedArea = Math.Min(
                solidArea,
                (solidArea - (Math.PI * innerRadius * innerRadius))
                    + (Math.PI * innerRadius * innerRadius * profile.Support.Tree.HollowInteriorDensity));
            double effectiveWidth = depositedArea / profile.LayerHeightMm;
            paths.Add(new ExtrusionPath(
                [new Vector3(segment.Upper, segment.UpperZ), new Vector3(segment.Lower, segment.LowerZ)],
                effectiveWidth,
                profile.LayerHeightMm,
                ExtrusionRole.SupportSheath,
                profile.Support.BodyMaterialId));
            envelopeMm3 += solidArea * profile.LayerHeightMm;
        }

        foreach (IGrouping<int, Tip> tipLayer in tips
            .Where(tip => validTipIds.Contains(tip.Id))
            .GroupBy(tip => tip.ContactLayer))
        {
            PathsD patches = PolygonMath.Union(tipLayer
                .Select(tip => PolygonMath.ToPaths([Square(tip.Position, profile.Support.Tree.MinimumRadiusMm)]))
                .ToArray());
            PathsD demand = PolygonMath.Union(tipLayer
                .Select(tip => PolygonMath.ToPaths(model.SupportDemandMasks[tip.DemandIndex].Rings))
                .ToArray());
            PathsD interfaceRegion = Clipper.Intersect(patches, demand, FillRule.NonZero, 3);
            paths.AddRange(InfillPathPlanner.Build(
                PolygonMath.ToRings(interfaceRegion),
                layers[tipLayer.Key].ZMm,
                profile.LayerHeightMm,
                profile.Support.InterfaceLineWidthMm,
                profile.Support.InterfaceDensity,
                "rectilinear",
                tipLayer.Key,
                profile.Support.InterfaceMaterialId,
                ExtrusionRole.SupportInterface));
        }

        LayerMask[] unsatisfied = model.SupportDemandMasks
            .Where((_, index) => tips.Where(tip => tip.DemandIndex == index).Any(tip => invalidTipIds.Contains(tip.Id)))
            .ToArray();
        var overlay = segments
            .Where(segment => segment.TipIds.All(validTipIds.Contains))
            .GroupBy(segment => segment.LowerZ)
            .Select(group => new LayerMask(
                group.Key,
                group.Select(segment => Square(segment.Lower, profile.Support.Tree.MinimumRadiusMm)).ToArray()))
            .ToArray();
        double interfaceVolume = Volume(paths, ExtrusionRole.SupportInterface);
        double sheathVolume = Volume(paths, ExtrusionRole.SupportSheath);
        IReadOnlyList<SupportRegionReport> regions = paths.Count == 0
            ? []
            :
            [
                new SupportRegionReport(
                    "tree-support-0001",
                    envelopeMm3,
                    0,
                    interfaceVolume,
                    sheathVolume,
                    diagnostics.ToArray()),
            ];
        return new SupportPlan(paths, regions, unsatisfied, overlay, diagnostics.ToArray());
    }

    private static List<Tip> CreateTips(
        IReadOnlyList<LayerMask> demands,
        IReadOnlyList<LayerContours> layers,
        ResolvedSimulationProfile profile)
    {
        var tips = new List<Tip>();
        int id = 0;
        for (int demandIndex = 0; demandIndex < demands.Count; demandIndex++)
        {
            LayerMask demand = demands[demandIndex];
            int demandLayer = FindLayer(layers, demand.ZMm);
            int gapLayers = Math.Max(1, (int)Math.Ceiling(profile.Support.TopZGapMm / profile.LayerHeightMm));
            int contactLayer = demandLayer - gapLayers;
            if (contactLayer < 0)
            {
                continue;
            }

            foreach (PolygonRing ring in demand.Rings.Where(ring => !ring.IsHole))
            {
                float minimumX = ring.Points.Min(point => point.X);
                float maximumX = ring.Points.Max(point => point.X);
                float minimumY = ring.Points.Min(point => point.Y);
                float maximumY = ring.Points.Max(point => point.Y);
                int before = tips.Count;
                for (double y = minimumY + (profile.Support.Tree.TipSpacingMm / 2); y < maximumY; y += profile.Support.Tree.TipSpacingMm)
                {
                    for (double x = minimumX + (profile.Support.Tree.TipSpacingMm / 2); x < maximumX; x += profile.Support.Tree.TipSpacingMm)
                    {
                        Vector2 point = new((float)x, (float)y);
                        if (PolygonMath.Contains(demand.Rings, point))
                        {
                            tips.Add(new Tip(id++, demandIndex, contactLayer, point));
                        }
                    }
                }

                if (tips.Count == before)
                {
                    Vector2 centroid = new(ring.Points.Average(point => point.X), ring.Points.Average(point => point.Y));
                    tips.Add(new Tip(id++, demandIndex, contactLayer, centroid));
                }
            }
        }

        if (tips.Count == 0)
        {
            throw new SimulationGeometryException("tree_tip_sampling_failed", "Tree support demand produced no reachable contact samples.");
        }

        return tips;
    }

    private static List<Node> Merge(IReadOnlyList<Node> nodes, double mergeDistance)
    {
        var remaining = nodes.OrderBy(node => node.Position.X).ThenBy(node => node.Position.Y).ToList();
        var result = new List<Node>();
        while (remaining.Count > 0)
        {
            Node first = remaining[0];
            remaining.RemoveAt(0);
            int partnerIndex = remaining.FindIndex(node => Vector2.Distance(first.Position, node.Position) <= mergeDistance);
            if (partnerIndex < 0)
            {
                result.Add(first);
                continue;
            }

            Node second = remaining[partnerIndex];
            remaining.RemoveAt(partnerIndex);
            result.Add(new Node(
                (first.Position + second.Position) / 2,
                first.TipIds.Concat(second.TipIds).OrderBy(id => id).ToArray()));
        }

        return result;
    }

    private static bool Collides(Vector2 point, IReadOnlyList<PolygonRing> model, double clearance)
    {
        if (model.Count == 0)
        {
            return false;
        }

        return PolygonMath.Contains(
            PolygonMath.ToRings(PolygonMath.Offset(PolygonMath.ToPaths(model), clearance)),
            point);
    }

    private static PolygonRing Square(Vector2 center, double radius) => new(
        [
            new(center.X - (float)radius, center.Y - (float)radius),
            new(center.X + (float)radius, center.Y - (float)radius),
            new(center.X + (float)radius, center.Y + (float)radius),
            new(center.X - (float)radius, center.Y + (float)radius),
        ],
        false);

    private static int FindLayer(IReadOnlyList<LayerContours> layers, double zMm)
    {
        int index = Enumerable.Range(0, layers.Count).MinBy(candidate => Math.Abs(layers[candidate].ZMm - zMm));
        if (Math.Abs(layers[index].ZMm - zMm) > 0.001)
        {
            throw new SimulationGeometryException("support_layer_mismatch", "Tree demand does not map to a physical layer.");
        }

        return index;
    }

    private static double Volume(IEnumerable<ExtrusionPath> paths, ExtrusionRole role) => paths
        .Where(path => path.Role == role)
        .Sum(path => Enumerable.Range(1, path.Points.Count - 1)
            .Sum(index => Vector3.Distance(path.Points[index - 1], path.Points[index])) * path.WidthMm * path.HeightMm);

    private sealed record Tip(int Id, int DemandIndex, int ContactLayer, Vector2 Position);

    private sealed record Node(Vector2 Position, IReadOnlyList<int> TipIds);

    private sealed record BranchSegment(
        Vector2 Upper,
        Vector2 Lower,
        float UpperZ,
        float LowerZ,
        IReadOnlyList<int> TipIds);
}
