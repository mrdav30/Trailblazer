//=======================================================================
// NavigationGuideRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Owns one scheduled acquisition and any successful lease until taken or disposed.</summary>
/// <remarks>Runtime-only. Submit and cancel in deterministic host order; no background work is performed.
/// Any raw GridWorld change between activation and transfer invalidates this acquisition, even if unrelated.
/// After transfer, the existing lease's normal dependency-validation rules apply.</remarks>
public sealed class NavigationGuideRequest : IDisposable
{
    internal NavigationGuideRequest(NavigationGuideScheduler owner, PathQuery query, long descriptorBytes)
    {
        Owner = owner;
        Query = query;
        Algorithm = query.Algorithm;
        DescriptorBytes = descriptorBytes;
    }

    /// <summary>Gets the requested search family.</summary>
    public PathAlgorithm Algorithm { get; }

    /// <summary>Gets the latest acquisition outcome; successful transfer preserves Success.</summary>
    /// <remarks>A completed result is dependency-validated again when taken, which can change this status to Stale.</remarks>
    public NavigationGuideStatus Status { get; internal set; } = NavigationGuideStatus.Pending;

    /// <summary>Transfers the A* lease once; returns false when pending, unsuccessful, already released or the wrong family.</summary>
    public bool TryTakeGuide(out NavigationGuideLease? result)
    {
        result = null;
        NavigationGuideScheduler? owner = Owner;
        return owner != null && owner.TryTakeGuide(this, out result);
    }

    /// <summary>Transfers the Flow lease once; returns false when pending, unsuccessful, already released or the wrong family.</summary>
    public bool TryTakeFlowField(out NavigationFlowFieldLease? result)
    {
        result = null;
        NavigationGuideScheduler? owner = Owner;
        return owner != null && owner.TryTakeFlowField(this, out result);
    }

    /// <summary>Cancels pending work and releases retained capacity and any unclaimed lease. Completed outcomes are preserved.</summary>
    public void Dispose() => Owner?.Release(this);

    internal NavigationGuideScheduler? Owner { get; set; }
    internal NavigationGuideRequest? Previous { get; set; }
    internal NavigationGuideRequest? Next { get; set; }
    internal PathQuery Query { get; set; }
    internal long DescriptorBytes { get; }
    internal ulong WorldChangeSequence { get; set; }
    internal NavigationGuideRequestPhase Phase { get; set; }
    internal NavigationGuideLease? Guide { get; set; }
    internal NavigationFlowFieldLease? FlowField { get; set; }
}
