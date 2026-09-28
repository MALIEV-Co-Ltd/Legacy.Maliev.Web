// <copyright file="InfillPathPlanner.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

/// <summary>Builds deterministic clipped rectilinear, grid, and implicit-gyroid paths.</summary>
public static class InfillPathPlanner
{
    /// <summary>Builds paths whose line-volume fraction matches the requested deposited fraction.</summary>
    public static IReadOnlyList<ExtrusionPath> Build(
        IReadOnlyList<PolygonRing> region,
        double zMm,
        double layerHeightMm,
        double lineWidthMm,
        double density,
        string pattern,
        int phase,
        string materialId,
        ExtrusionRole role)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialId);
        if (region.Count == 0 || density <= 0)
        {
            return [];
        }

        if (!double.IsFinite(zMm) || !double.IsFinite(layerHeightMm) || layerHeightMm <= 0
            || !double.IsFinite(lineWidthMm) || lineWidthMm <= 0
            || !double.IsFinite(density) || density > 1)
        {
            throw new SimulationGeometryException("path_setting_invalid", "Infill geometry settings must be finite and physically bounded.");
        }

        string normalized = pattern.ToLowerInvariant();
        List<(Vector2 Start, Vector2 End)> segments = normalized switch
        {
            "rectilinear" => Linear(region, lineWidthMm / density, vertical: (phase & 1) == 1),
            "grid" => Grid(region, (2 * lineWidthMm) / density),
            "gyroid" => Gyroid(region, zMm, lineWidthMm, density, phase),
            _ => throw new SimulationGeometryException("infill_pattern_unsupported", $"Infill pattern '{pattern}' is not supported."),
        };

        return segments
            .Where(segment => Vector2.DistanceSquared(segment.Start, segment.End) > 1e-12)
            .Select(segment => new ExtrusionPath(
                [new Vector3(segment.Start, (float)zMm), new Vector3(segment.End, (float)zMm)],
                lineWidthMm,
                layerHeightMm,
                role,
                materialId))
            .ToArray();
    }

    private static List<(Vector2 Start, Vector2 End)> Grid(IReadOnlyList<PolygonRing> region, double spacing)
    {
        List<(Vector2 Start, Vector2 End)> result = Linear(region, spacing, vertical: false);
        result.AddRange(Linear(region, spacing, vertical: true));
        return result;
    }

    private static List<(Vector2 Start, Vector2 End)> Linear(
        IReadOnlyList<PolygonRing> region,
        double spacing,
        bool vertical)
    {
        Bounds bounds = Bounds.Create(region);
        double minimum = vertical ? bounds.MinimumX : bounds.MinimumY;
        double maximum = vertical ? bounds.MaximumX : bounds.MaximumY;
        var result = new List<(Vector2 Start, Vector2 End)>();
        for (double coordinate = minimum + (spacing / 2); coordinate < maximum; coordinate += spacing)
        {
            var intersections = new List<double>();
            foreach (PolygonRing ring in region)
            {
                for (int index = 0; index < ring.Points.Count; index++)
                {
                    Vector2 first = ring.Points[index];
                    Vector2 second = ring.Points[(index + 1) % ring.Points.Count];
                    double firstAxis = vertical ? first.X : first.Y;
                    double secondAxis = vertical ? second.X : second.Y;
                    if (!((firstAxis <= coordinate && secondAxis > coordinate)
                        || (secondAxis <= coordinate && firstAxis > coordinate)))
                    {
                        continue;
                    }

                    double fraction = (coordinate - firstAxis) / (secondAxis - firstAxis);
                    intersections.Add(vertical
                        ? first.Y + ((second.Y - first.Y) * fraction)
                        : first.X + ((second.X - first.X) * fraction));
                }
            }

            intersections.Sort();
            for (int index = 0; index + 1 < intersections.Count; index += 2)
            {
                Vector2 start = vertical
                    ? new Vector2((float)coordinate, (float)intersections[index])
                    : new Vector2((float)intersections[index], (float)coordinate);
                Vector2 end = vertical
                    ? new Vector2((float)coordinate, (float)intersections[index + 1])
                    : new Vector2((float)intersections[index + 1], (float)coordinate);
                result.Add((start, end));
            }
        }

        return result;
    }

    private static List<(Vector2 Start, Vector2 End)> Gyroid(
        IReadOnlyList<PolygonRing> region,
        double zMm,
        double lineWidthMm,
        double density,
        int phase)
    {
        Bounds bounds = Bounds.Create(region);
        double targetLength = PolygonMath.NetArea(region) * density / lineWidthMm;
        double low = Math.Max(lineWidthMm * 2, 0.2);
        double high = Math.Max(bounds.Width, bounds.Height) * 2;
        List<(Vector2 Start, Vector2 End)> best = [];
        double bestError = double.MaxValue;
        for (int iteration = 0; iteration < 14; iteration++)
        {
            double period = (low + high) / 2;
            List<(Vector2 Start, Vector2 End)> candidate = MarchAndClip(region, bounds, zMm, period, lineWidthMm, phase);
            double length = candidate.Sum(segment => Vector2.Distance(segment.Start, segment.End));
            double error = Math.Abs(length - targetLength);
            if (error < bestError)
            {
                best = candidate;
                bestError = error;
            }

            if (length > targetLength)
            {
                low = period;
            }
            else
            {
                high = period;
            }
        }

        return TrimToLength(best, targetLength);
    }

    private static List<(Vector2 Start, Vector2 End)> MarchAndClip(
        IReadOnlyList<PolygonRing> region,
        Bounds bounds,
        double zMm,
        double period,
        double lineWidthMm,
        int phase)
    {
        double step = Math.Min(lineWidthMm / 2, period / 20);
        int columns = Math.Max(1, (int)Math.Ceiling(bounds.Width / step));
        int rows = Math.Max(1, (int)Math.Ceiling(bounds.Height / step));
        double dx = bounds.Width / columns;
        double dy = bounds.Height / rows;
        var result = new List<(Vector2 Start, Vector2 End)>();
        for (int row = 0; row < rows; row++)
        {
            double y0 = bounds.MinimumY + (row * dy);
            double y1 = y0 + dy;
            for (int column = 0; column < columns; column++)
            {
                double x0 = bounds.MinimumX + (column * dx);
                double x1 = x0 + dx;
                Vector2[] corners = [new((float)x0, (float)y0), new((float)x1, (float)y0), new((float)x1, (float)y1), new((float)x0, (float)y1)];
                double[] values = corners.Select(point => Field(point.X, point.Y, zMm, period, phase)).ToArray();
                var crossings = new List<Vector2>(4);
                for (int edge = 0; edge < 4; edge++)
                {
                    double first = values[edge];
                    double second = values[(edge + 1) % 4];
                    if ((first < 0) == (second < 0))
                    {
                        continue;
                    }

                    double fraction = first / (first - second);
                    crossings.Add(Vector2.Lerp(corners[edge], corners[(edge + 1) % 4], (float)fraction));
                }

                if (crossings.Count == 2)
                {
                    result.AddRange(ClipSegment(crossings[0], crossings[1], region));
                }
                else if (crossings.Count == 4)
                {
                    bool centerPositive = Field((x0 + x1) / 2, (y0 + y1) / 2, zMm, period, phase) >= 0;
                    int firstPair = centerPositive ? 1 : 0;
                    result.AddRange(ClipSegment(crossings[firstPair], crossings[(firstPair + 1) % 4], region));
                    result.AddRange(ClipSegment(crossings[(firstPair + 2) % 4], crossings[(firstPair + 3) % 4], region));
                }
            }
        }

        return result;
    }

    private static double Field(double x, double y, double z, double period, int phase)
    {
        double scale = 2 * Math.PI / period;
        double shiftedZ = (z * scale) + (phase * Math.PI / 7);
        double sx = x * scale;
        double sy = y * scale;
        return (Math.Sin(sx) * Math.Cos(sy))
            + (Math.Sin(sy) * Math.Cos(shiftedZ))
            + (Math.Sin(shiftedZ) * Math.Cos(sx));
    }

    private static IEnumerable<(Vector2 Start, Vector2 End)> ClipSegment(
        Vector2 start,
        Vector2 end,
        IReadOnlyList<PolygonRing> region)
    {
        var values = new List<double> { 0, 1 };
        Vector2 direction = end - start;
        foreach (PolygonRing ring in region)
        {
            for (int index = 0; index < ring.Points.Count; index++)
            {
                Vector2 edgeStart = ring.Points[index];
                Vector2 edgeDirection = ring.Points[(index + 1) % ring.Points.Count] - edgeStart;
                double denominator = Cross(direction, edgeDirection);
                if (Math.Abs(denominator) < 1e-12)
                {
                    continue;
                }

                Vector2 delta = edgeStart - start;
                double t = Cross(delta, edgeDirection) / denominator;
                double u = Cross(delta, direction) / denominator;
                if (t > 0 && t < 1 && u >= 0 && u <= 1)
                {
                    values.Add(t);
                }
            }
        }

        double[] ordered = values.Distinct().OrderBy(value => value).ToArray();
        for (int index = 0; index + 1 < ordered.Length; index++)
        {
            double first = ordered[index];
            double second = ordered[index + 1];
            Vector2 midpoint = start + (direction * (float)((first + second) / 2));
            if (PolygonMath.Contains(region, midpoint))
            {
                yield return (start + (direction * (float)first), start + (direction * (float)second));
            }
        }
    }

    private static List<(Vector2 Start, Vector2 End)> TrimToLength(
        IReadOnlyList<(Vector2 Start, Vector2 End)> segments,
        double targetLength)
    {
        var result = new List<(Vector2 Start, Vector2 End)>();
        double remaining = targetLength;
        foreach ((Vector2 start, Vector2 end) in segments)
        {
            double length = Vector2.Distance(start, end);
            if (remaining <= 0)
            {
                break;
            }

            if (length <= remaining)
            {
                result.Add((start, end));
                remaining -= length;
            }
            else
            {
                result.Add((start, Vector2.Lerp(start, end, (float)(remaining / length))));
                remaining = 0;
            }
        }

        return result;
    }

    private static double Cross(Vector2 left, Vector2 right) => ((double)left.X * right.Y) - ((double)left.Y * right.X);

    private readonly record struct Bounds(double MinimumX, double MinimumY, double MaximumX, double MaximumY)
    {
        public double Width => this.MaximumX - this.MinimumX;

        public double Height => this.MaximumY - this.MinimumY;

        public static Bounds Create(IReadOnlyList<PolygonRing> rings) => new(
            rings.SelectMany(ring => ring.Points).Min(point => point.X),
            rings.SelectMany(ring => ring.Points).Min(point => point.Y),
            rings.SelectMany(ring => ring.Points).Max(point => point.X),
            rings.SelectMany(ring => ring.Points).Max(point => point.Y));
    }
}
