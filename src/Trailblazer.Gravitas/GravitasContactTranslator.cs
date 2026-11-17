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
        Func<LSCollider, int>? carrierIdResolver,
        ref SupportQueryPolicy policy,
        out NavigationSupportStatus supportStatus)
    {
        supportStatus = NavigationSupportStatus.NoSupport;
        Physics3DHit hit;
        if (policy.Scratch is { } scratch)
        {
            var sampled = SweepBounded(body, acceptedRoot, rootToFootOffset, supportLayers,
                supportProbeRadius, supportProbeOffset, supportProbeDistance, scratch, ref policy);
            if (policy.ExecutionStatus != PhysicsQueryExecutionStatus.Completed)
            {
                supportStatus = NavigationSupportStatus.Unavailable;
                return default;
            }
            if (sampled is not Physics3DHit certified)
                return default;
            hit = certified;
        }
        else
        {
            Vector3d acceptedFoot = acceptedRoot + Vector3d.Down * rootToFootOffset;
            Vector3d origin = acceptedFoot + Vector3d.Up * supportProbeOffset;
            if (!body.Context.Query3D.SweepSphere(origin, supportProbeRadius, Vector3d.Down,
                supportProbeDistance, out hit, supportLayers, body.Collider))
                return default;
        }

        if (hit.Normal.Y <= Fixed64.Zero)
            return default;
        if (!hit.TryGetPoint(out Vector3d point))
        {
            if (policy.Scratch != null)
                supportStatus = NavigationSupportStatus.Unavailable;
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
        supportStatus = NavigationSupportStatus.Found;
        return new GravitasContact(
            point.Y,
            hit.Normal,
            collider.Material.DynamicFriction,
            platform);
    }

    private static Physics3DHit? SweepBounded(
        SolidBody body, Vector3d acceptedRoot, Fixed64 rootToFootOffset, PhysicsLayerMask supportLayers,
        Fixed64 supportProbeRadius, Fixed64 supportProbeOffset, Fixed64 supportProbeDistance,
        PhysicsQueryScratch scratch, ref SupportQueryPolicy policy)
    {
        if (!Fixed64.TrySubtract(acceptedRoot.Y, rootToFootOffset, out var footY)
            || !Fixed64.TryAdd(footY, supportProbeOffset, out var originY))
        {
            policy.Record(PhysicsQueryExecutionStatus.Unrepresentable, default);
            return null;
        }

        var origin = new Vector3d(acceptedRoot.X, originY, acceptedRoot.Z);
        var status = body.Context.Query3D.SweepSphere(origin, supportProbeRadius, Vector3d.Down, supportProbeDistance,
            supportLayers, body.Collider, policy.Budget, scratch, out var sampled, out var work);
        policy.Record(status, work);
        return sampled;
    }
}
