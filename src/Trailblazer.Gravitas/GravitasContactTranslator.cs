//=======================================================================
// GravitasContactTranslator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler.Diagnostics;
using FixedMathSharp;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.Queries;
using Gravitas.Support;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Navigation;

/// <summary>Translates Gravitas collision contacts into navigator support samples.</summary>
internal static class GravitasContactTranslator
{
    internal static GravitasContact Sample(
        SolidBody body,
        Vector3d acceptedRoot,
        Fixed64 rootToFootOffset,
        PhysicsLayerMask supportLayers,
        Fixed64 supportProbeRadius,
        Fixed64 supportProbeOffset,
        Fixed64 supportProbeDistance,
        Func<LSCollider, int>? carrierIdResolver)
    {
        Vector3d acceptedFoot = acceptedRoot + Vector3d.Down * rootToFootOffset;
        Vector3d origin = acceptedFoot + Vector3d.Up * supportProbeOffset;
        if (!body.Context.Query3D.SweepSphere(
                origin,
                supportProbeRadius,
                Vector3d.Down,
                supportProbeDistance,
                out Physics3DHit hit,
                supportLayers,
                body.Collider)
            || hit.Normal.Y <= Fixed64.Zero
            || !hit.TryGetPoint(out Vector3d point))
        {
            return default;
        }

        // A successful Gravitas query is produced from a registered collider.
        LSCollider collider = hit.Collider!;
        int carrierId = carrierIdResolver?.Invoke(collider) ?? 0;
        ChronicleGuard.ThrowIfArgument(
            carrierId < 0,
            nameof(carrierIdResolver),
            "Carrier identifiers must be zero or positive.");
        ChronicleGuard.ThrowIfTrue(
            !collider.Transform.TryGetLocalToWorldMatrix(out Fixed4x4 transform),
            nameof(FixedTransform),
            "Supporting transform cannot be represented as a deterministic matrix.");

        PlatformSnapshot platform = new(
            carrierId,
            transform,
            inert: carrierId == 0);
        return new GravitasContact(
            point.Y,
            hit.Normal,
            collider.Material.DynamicFriction,
            platform);
    }
}
