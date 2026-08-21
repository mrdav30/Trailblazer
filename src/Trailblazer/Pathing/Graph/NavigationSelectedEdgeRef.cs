//=======================================================================
// NavigationSelectedEdgeRef.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Identifies one canonical outgoing edge without retaining snapshot-local state.</summary>
internal readonly struct NavigationSelectedEdgeRef : IEquatable<NavigationSelectedEdgeRef>
{
    internal NavigationSelectedEdgeRef(
        NavigationCellAddress target,
        TraversalMedium targetMedium,
        int canonicalOutgoingOrdinal)
    {
        Target = target;
        TargetMedium = targetMedium;
        CanonicalOutgoingOrdinal = canonicalOutgoingOrdinal;
    }

    internal NavigationCellAddress Target { get; }

    internal TraversalMedium TargetMedium { get; }

    internal int CanonicalOutgoingOrdinal { get; }

    internal bool IsValid => !string.IsNullOrEmpty(Target.MapId)
        && NavigationCell.IsKnownMedium(TargetMedium)
        && CanonicalOutgoingOrdinal >= 0;

    public bool Equals(NavigationSelectedEdgeRef other) =>
        Target.Equals(other.Target)
        && TargetMedium == other.TargetMedium
        && CanonicalOutgoingOrdinal == other.CanonicalOutgoingOrdinal;

    public override bool Equals(object? obj) =>
        obj is NavigationSelectedEdgeRef other && Equals(other);

    public override int GetHashCode() => SwiftHashTools.CombineHashCodes(
        SwiftHashTools.CombineHashCodes(Target.GetHashCode(), (int)TargetMedium),
        CanonicalOutgoingOrdinal);

    public static bool operator ==(
        NavigationSelectedEdgeRef left,
        NavigationSelectedEdgeRef right) => left.Equals(right);

    public static bool operator !=(
        NavigationSelectedEdgeRef left,
        NavigationSelectedEdgeRef right) => !left.Equals(right);
}
