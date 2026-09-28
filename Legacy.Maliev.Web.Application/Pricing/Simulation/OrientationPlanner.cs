// <copyright file="OrientationPlanner.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

/// <summary>Generates a bounded deterministic build-pose search set.</summary>
public static class OrientationPlanner
{
    /// <summary>Generates source, axis, and planar-diagonal candidates or returns the locked pose alone.</summary>
    public static IReadOnlyList<Pose> GenerateCandidates(NormalizedMesh mesh, Pose? fixedPose = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (fixedPose is not null)
        {
            return [fixedPose];
        }

        (string Id, Matrix4x4 Transform)[] definitions =
        [
            ("00-source", Matrix4x4.Identity),
            ("axis-x+90", Matrix4x4.CreateRotationX(MathF.PI / 2)),
            ("axis-x-90", Matrix4x4.CreateRotationX(-MathF.PI / 2)),
            ("axis-x180", Matrix4x4.CreateRotationX(MathF.PI)),
            ("axis-y+90", Matrix4x4.CreateRotationY(MathF.PI / 2)),
            ("axis-y-90", Matrix4x4.CreateRotationY(-MathF.PI / 2)),
            ("axis-y180", Matrix4x4.CreateRotationY(MathF.PI)),
            ("z-45", Matrix4x4.CreateRotationZ(-MathF.PI / 4)),
            ("z+45", Matrix4x4.CreateRotationZ(MathF.PI / 4)),
            ("z+90", Matrix4x4.CreateRotationZ(MathF.PI / 2)),
            ("z-90", Matrix4x4.CreateRotationZ(-MathF.PI / 2)),
            ("x+90-z+45", Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateRotationZ(MathF.PI / 4)),
            ("y+90-z+45", Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateRotationZ(MathF.PI / 4)),
        ];
        return definitions
            .Select(item => new Pose(item.Id, item.Transform))
            .GroupBy(pose => QuantizedTransform(pose.Transform), StringComparer.Ordinal)
            .Select(group => group.OrderBy(pose => pose.Id, StringComparer.Ordinal).First())
            .OrderBy(pose => pose.Id, StringComparer.Ordinal)
            .Take(32)
            .ToArray();
    }

    private static string QuantizedTransform(Matrix4x4 value) => string.Join(
        ',',
        new[]
        {
            value.M11, value.M12, value.M13, value.M14,
            value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34,
            value.M41, value.M42, value.M43, value.M44,
        }.Select(component => Math.Round(component, 6).ToString("F6", System.Globalization.CultureInfo.InvariantCulture)));
}
