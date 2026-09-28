// <copyright file="ModelPathPlanner.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using Clipper2Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

/// <summary>Plans non-overlapping model deposition and bridge/support classifications.</summary>
public static class ModelPathPlanner
{
    /// <summary>Builds walls, skins, gap fill, sparse infill, and bridge paths.</summary>
    public static ModelPathPlan Build(IReadOnlyList<LayerContours> layers, ResolvedSimulationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(profile);
        var paths = new List<ExtrusionPath>();
        var bridges = new List<LayerMask>();
        var supportDemand = new List<LayerMask>();
        var diagnostics = new SortedSet<string>(StringComparer.Ordinal);
        PathsD[] solids = layers.Select(layer => PolygonMath.ToPaths(layer.Rings)).ToArray();
        (PathsD[] bottomMasks, PathsD[] topMasks) = BuildSkinMasks(solids, profile);

        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            LayerContours layer = layers[layerIndex];
            PathsD solid = solids[layerIndex];
            IReadOnlyList<PolygonRing> previousRings = layerIndex == 0 ? [] : layers[layerIndex - 1].Rings;
            AddWalls(paths, solid, previousRings, layer, profile, diagnostics);

            double occupiedWidth = profile.OuterWallLineWidthMm
                + (Math.Max(0, profile.WallCount - 1) * profile.InnerWallLineWidthMm);
            PathsD interior = PolygonMath.Offset(solid, -occupiedWidth);
            PathsD bridgeRegion = [];
            if (layerIndex > 0)
            {
                PathsD previous = solids[layerIndex - 1];
                double angleRadians = profile.Support.OverhangAngleFromHorizontalDegrees * Math.PI / 180;
                double lateralAllowance = layer.ThicknessMm / Math.Tan(angleRadians);
                PathsD printableReach = PolygonMath.Offset(previous, lateralAllowance);
                PathsD unsupported = Clipper.Difference(solid, printableReach, FillRule.NonZero, 3);
                if (unsupported.Count > 0)
                {
                    if (IsTwoAnchorBridge(unsupported, previous, profile, lateralAllowance))
                    {
                        bridgeRegion = Clipper.Intersect(interior, unsupported, FillRule.NonZero, 3);
                        IReadOnlyList<PolygonRing> bridgeRings = PolygonMath.ToRings(unsupported);
                        bridges.Add(new LayerMask(layer.ZMm, bridgeRings));
                        paths.AddRange(InfillPathPlanner.Build(
                            PolygonMath.ToRings(bridgeRegion),
                            layer.ZMm,
                            layer.ThicknessMm,
                            profile.SolidInfillLineWidthMm,
                            1,
                            "rectilinear",
                            layerIndex,
                            profile.MaterialId,
                            ExtrusionRole.Bridge));
                    }
                    else
                    {
                        supportDemand.Add(new LayerMask(layer.ZMm, PolygonMath.ToRings(unsupported)));
                    }
                }
            }

            PathsD available = bridgeRegion.Count == 0
                ? interior
                : Clipper.Difference(interior, bridgeRegion, FillRule.NonZero, 3);
            PathsD bottom = Clipper.Intersect(available, bottomMasks[layerIndex], FillRule.NonZero, 3);
            PathsD top = Clipper.Intersect(available, topMasks[layerIndex], FillRule.NonZero, 3);
            top = Clipper.Difference(top, bottom, FillRule.NonZero, 3);
            PathsD skin = PolygonMath.Union(bottom, top);
            PathsD sparse = Clipper.Difference(available, skin, FillRule.NonZero, 3);
            if (bottom.Count > 0)
            {
                paths.AddRange(InfillPathPlanner.Build(
                    PolygonMath.ToRings(bottom),
                    layer.ZMm,
                    layer.ThicknessMm,
                    profile.SolidInfillLineWidthMm,
                    1,
                    "rectilinear",
                    layerIndex,
                    profile.MaterialId,
                    ExtrusionRole.BottomSkin));
            }

            if (top.Count > 0)
            {
                paths.AddRange(InfillPathPlanner.Build(
                    PolygonMath.ToRings(top),
                    layer.ZMm,
                    layer.ThicknessMm,
                    profile.SolidInfillLineWidthMm,
                    1,
                    "rectilinear",
                    layerIndex,
                    profile.MaterialId,
                    ExtrusionRole.TopSkin));
            }

            if (sparse.Count > 0)
            {
                paths.AddRange(InfillPathPlanner.Build(
                    PolygonMath.ToRings(sparse),
                    layer.ZMm,
                    layer.ThicknessMm,
                    profile.SparseInfillLineWidthMm,
                    profile.SparseInfillFraction,
                    profile.SparseInfillPattern,
                    layerIndex,
                    profile.MaterialId,
                    ExtrusionRole.SparseInfill));
            }
        }

        return new ModelPathPlan(paths, bridges, supportDemand, diagnostics.ToArray());
    }

    private static (PathsD[] Bottom, PathsD[] Top) BuildSkinMasks(
        IReadOnlyList<PathsD> solids,
        ResolvedSimulationProfile profile)
    {
        PathsD[] bottom = Enumerable.Range(0, solids.Count).Select(_ => new PathsD()).ToArray();
        PathsD[] top = Enumerable.Range(0, solids.Count).Select(_ => new PathsD()).ToArray();
        for (int layer = 0; layer < solids.Count; layer++)
        {
            PathsD bottomSeed = layer == 0
                ? solids[layer]
                : Clipper.Difference(solids[layer], solids[layer - 1], FillRule.NonZero, 3);
            for (int target = layer; target < Math.Min(solids.Count, layer + profile.BottomShellLayers); target++)
            {
                bottom[target] = PolygonMath.Union(
                    bottom[target],
                    Clipper.Intersect(solids[target], bottomSeed, FillRule.NonZero, 3));
            }

            PathsD topSeed = layer == solids.Count - 1
                ? solids[layer]
                : Clipper.Difference(solids[layer], solids[layer + 1], FillRule.NonZero, 3);
            for (int target = layer; target >= Math.Max(0, layer - profile.TopShellLayers + 1); target--)
            {
                top[target] = PolygonMath.Union(
                    top[target],
                    Clipper.Intersect(solids[target], topSeed, FillRule.NonZero, 3));
            }
        }

        return (bottom, top);
    }

    private static void AddWalls(
        List<ExtrusionPath> output,
        PathsD solid,
        IReadOnlyList<PolygonRing> previousRings,
        LayerContours layer,
        ResolvedSimulationProfile profile,
        SortedSet<string> diagnostics)
    {
        int produced = 0;
        for (int wall = 0; wall < profile.WallCount; wall++)
        {
            double distance = wall == 0
                ? profile.OuterWallLineWidthMm / 2
                : profile.OuterWallLineWidthMm + ((wall - 1) * profile.InnerWallLineWidthMm) + (profile.InnerWallLineWidthMm / 2);
            PathsD centerlines = PolygonMath.Offset(solid, -distance);
            if (centerlines.Count == 0)
            {
                break;
            }

            ExtrusionRole role = wall == 0 ? ExtrusionRole.OuterWall : ExtrusionRole.InnerWall;
            double width = wall == 0 ? profile.OuterWallLineWidthMm : profile.InnerWallLineWidthMm;
            foreach (PathD centerline in centerlines)
            {
                if (previousRings.Count == 0)
                {
                    output.Add(ClosedPath(centerline, layer, width, role, profile.MaterialId));
                }
                else
                {
                    AddSplitWallSegments(output, centerline, previousRings, layer, width, role, profile);
                }
            }

            produced++;
        }

        if (produced >= profile.WallCount)
        {
            return;
        }

        foreach (PathD polygon in solid)
        {
            RectD bounds = Clipper.GetBounds(polygon);
            double narrow = Math.Min(bounds.Width, bounds.Height);
            double residual = narrow - profile.OuterWallLineWidthMm;
            if (residual >= profile.GapFillMinimumWidthMm && residual <= profile.GapFillMaximumWidthMm)
            {
                bool vertical = bounds.Height >= bounds.Width;
                Vector3 start = vertical
                    ? new Vector3((float)((bounds.left + bounds.right) / 2), (float)bounds.top, (float)layer.ZMm)
                    : new Vector3((float)bounds.left, (float)((bounds.top + bounds.bottom) / 2), (float)layer.ZMm);
                Vector3 end = vertical
                    ? new Vector3((float)((bounds.left + bounds.right) / 2), (float)bounds.bottom, (float)layer.ZMm)
                    : new Vector3((float)bounds.right, (float)((bounds.top + bounds.bottom) / 2), (float)layer.ZMm);
                output.Add(new ExtrusionPath(
                    [start, end],
                    residual,
                    layer.ThicknessMm,
                    ExtrusionRole.GapFill,
                    profile.MaterialId));
            }
            else
            {
                diagnostics.Add("unresolved_narrow_paths");
            }
        }
    }

    private static void AddSplitWallSegments(
        List<ExtrusionPath> output,
        PathD centerline,
        IReadOnlyList<PolygonRing> previousRings,
        LayerContours layer,
        double width,
        ExtrusionRole role,
        ResolvedSimulationProfile profile)
    {
        for (int index = 0; index < centerline.Count; index++)
        {
            Vector2 start = new((float)centerline[index].x, (float)centerline[index].y);
            Vector2 end = new((float)centerline[(index + 1) % centerline.Count].x, (float)centerline[(index + 1) % centerline.Count].y);
            foreach ((Vector2 first, Vector2 second, bool supported) in PolygonMath.SplitByRegion(start, end, previousRings))
            {
                output.Add(new ExtrusionPath(
                    [new Vector3(first, (float)layer.ZMm), new Vector3(second, (float)layer.ZMm)],
                    width,
                    layer.ThicknessMm,
                    role,
                    profile.MaterialId,
                    supported ? 1 : profile.OverhangSpeedMultiplier));
            }
        }
    }

    private static bool IsTwoAnchorBridge(
        PathsD unsupported,
        PathsD previous,
        ResolvedSimulationProfile profile,
        double lateralAllowance)
    {
        RectD gap = Clipper.GetBounds(unsupported);
        if (Math.Min(gap.Width, gap.Height) > profile.BridgeMaximumSpanMm)
        {
            return false;
        }

        double epsilon = lateralAllowance + 0.01;
        bool left = previous.Any(path => Math.Abs(Clipper.GetBounds(path).right - gap.left) <= epsilon);
        bool right = previous.Any(path => Math.Abs(Clipper.GetBounds(path).left - gap.right) <= epsilon);
        bool top = previous.Any(path => Math.Abs(Clipper.GetBounds(path).bottom - gap.top) <= epsilon);
        bool bottom = previous.Any(path => Math.Abs(Clipper.GetBounds(path).top - gap.bottom) <= epsilon);
        double anchor = profile.BridgeAnchorLengthMm;
        bool horizontalAnchors = left && right && gap.Height >= anchor;
        bool verticalAnchors = top && bottom && gap.Width >= anchor;
        return horizontalAnchors || verticalAnchors;
    }

    private static ExtrusionPath ClosedPath(
        PathD polygon,
        LayerContours layer,
        double width,
        ExtrusionRole role,
        string materialId)
    {
        var points = polygon.Select(point => new Vector3((float)point.x, (float)point.y, (float)layer.ZMm)).ToList();
        points.Add(points[0]);
        return new ExtrusionPath(points, width, layer.ThicknessMm, role, materialId);
    }
}

/// <summary>Shared deterministic polygon conversion and containment helpers.</summary>
internal static class PolygonMath
{
    public static PathsD ToPaths(IReadOnlyList<PolygonRing> rings)
    {
        var result = new PathsD(rings.Count);
        foreach (PolygonRing ring in rings)
        {
            var path = new PathD(ring.Points.Select(point => new PointD(point.X, point.Y)));
            bool positive = Clipper.IsPositive(path);
            if (positive == ring.IsHole)
            {
                path.Reverse();
            }

            result.Add(path);
        }

        return result;
    }

    public static IReadOnlyList<PolygonRing> ToRings(PathsD paths) => paths.Select(path => new PolygonRing(
        path.Select(point => new Vector2((float)point.x, (float)point.y)).ToArray(),
        !Clipper.IsPositive(path))).ToArray();

    public static PathsD Offset(PathsD paths, double distance) => paths.Count == 0
        ? []
        : Clipper.InflatePaths(paths, distance, JoinType.Miter, EndType.Polygon, 2, 3, 0);

    public static PathsD Union(params PathsD[] groups)
    {
        var combined = new PathsD();
        foreach (PathsD group in groups)
        {
            combined.AddRange(group);
        }

        if (combined.Count == 0)
        {
            return [];
        }

        var clipper = new ClipperD(3);
        clipper.AddSubject(combined);
        var result = new PathsD();
        if (!clipper.Execute(ClipType.Union, FillRule.NonZero, result))
        {
            throw new SimulationGeometryException("geometry_boolean_failed", "Skin mask union failed.");
        }

        return result;
    }

    public static IEnumerable<(Vector2 Start, Vector2 End, bool Supported)> SplitByRegion(
        Vector2 start,
        Vector2 end,
        IReadOnlyList<PolygonRing> rings)
    {
        Vector2 direction = end - start;
        var values = new List<double> { 0, 1 };
        foreach (PolygonRing ring in rings)
        {
            for (int index = 0; index < ring.Points.Count; index++)
            {
                Vector2 edgeStart = ring.Points[index];
                Vector2 edge = ring.Points[(index + 1) % ring.Points.Count] - edgeStart;
                double denominator = Cross(direction, edge);
                if (Math.Abs(denominator) < 1e-12)
                {
                    continue;
                }

                Vector2 delta = edgeStart - start;
                double pathFraction = Cross(delta, edge) / denominator;
                double edgeFraction = Cross(delta, direction) / denominator;
                if (pathFraction > 0 && pathFraction < 1 && edgeFraction >= 0 && edgeFraction <= 1)
                {
                    values.Add(pathFraction);
                }
            }
        }

        double[] ordered = values.Distinct().OrderBy(value => value).ToArray();
        for (int index = 0; index + 1 < ordered.Length; index++)
        {
            Vector2 first = start + (direction * (float)ordered[index]);
            Vector2 second = start + (direction * (float)ordered[index + 1]);
            Vector2 midpoint = (first + second) / 2;
            yield return (first, second, Contains(rings, midpoint));
        }
    }

    public static bool Contains(IReadOnlyList<PolygonRing> rings, Vector2 point)
    {
        bool inside = false;
        foreach (PolygonRing ring in rings)
        {
            bool current = false;
            for (int first = 0, second = ring.Points.Count - 1; first < ring.Points.Count; second = first++)
            {
                Vector2 a = ring.Points[first];
                Vector2 b = ring.Points[second];
                if (((a.Y > point.Y) != (b.Y > point.Y))
                    && point.X < (((b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y)) + a.X))
                {
                    current = !current;
                }
            }

            if (current)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public static double NetArea(IReadOnlyList<PolygonRing> rings) => rings.Sum(ring =>
        (ring.IsHole ? -1 : 1) * Math.Abs(Area(ring.Points)));

    private static double Area(IReadOnlyList<Vector2> points)
    {
        double sum = 0;
        for (int index = 0; index < points.Count; index++)
        {
            Vector2 current = points[index];
            Vector2 next = points[(index + 1) % points.Count];
            sum += ((double)current.X * next.Y) - ((double)next.X * current.Y);
        }

        return sum / 2;
    }

    private static double Cross(Vector2 left, Vector2 right) => ((double)left.X * right.Y) - ((double)left.Y * right.X);
}
