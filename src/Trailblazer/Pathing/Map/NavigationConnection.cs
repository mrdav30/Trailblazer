//=======================================================================
// NavigationConnection.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes one directed, source-map-owned physical connection or shortcut.
/// </summary>
public sealed class NavigationConnection : IEquatable<NavigationConnection>
{
    private readonly NavigationCellAddress[] _witnesses;
    private readonly ReadOnlyCollection<NavigationCellAddress> _witnessView;

    /// <summary>
    /// The stable map-local connection identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The local source-cell index in the owning map.
    /// </summary>
    public VoxelIndex SourceIndex { get; }

    /// <summary>
    /// The durable destination-cell address.
    /// </summary>
    public NavigationCellAddress Destination { get; }

    /// <summary>
    /// The exact world-space point where traversal leaves the source cell.
    /// </summary>
    public Vector3d EntryAnchor { get; }

    /// <summary>
    /// The exact world-space point where traversal enters the destination cell.
    /// </summary>
    public Vector3d ExitAnchor { get; }

    /// <summary>
    /// The inclusive horizontal radius clearance through the complete connection corridor.
    /// </summary>
    public Fixed64 PortalRadiusClearance { get; }

    /// <summary>
    /// The inclusive vertical clearance through the complete connection corridor.
    /// </summary>
    public Fixed64 PortalHeightClearance { get; }

    /// <summary>
    /// The cells whose presence, blockage, and clearance certify this connection.
    /// </summary>
    public IReadOnlyList<NavigationCellAddress> Witnesses => _witnessView;

    /// <summary>
    /// The non-negative surcharge added after geometric travel cost.
    /// </summary>
    public Fixed64 AdditionalCost { get; }

    /// <summary>
    /// Indicates that the complete connection cost certifies a Euclidean lower bound.
    /// </summary>
    public bool IsLowerBoundCertified { get; }

    /// <summary>
    /// Creates one complete directed connection definition.
    /// </summary>
    public NavigationConnection(
        string id,
        VoxelIndex sourceIndex,
        NavigationCellAddress destination,
        Vector3d entryAnchor,
        Vector3d exitAnchor,
        Fixed64 portalRadiusClearance,
        Fixed64 portalHeightClearance,
        IEnumerable<NavigationCellAddress>? witnesses = null,
        Fixed64 additionalCost = default,
        bool isLowerBoundCertified = false)
    {
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(id),
            nameof(id),
            "Connection ID cannot be null, empty, or whitespace.");
        SwiftThrowHelper.ThrowIfArgument(
            portalRadiusClearance < Fixed64.Zero,
            nameof(portalRadiusClearance),
            "Portal radius clearance must be non-negative.");
        SwiftThrowHelper.ThrowIfArgument(
            portalHeightClearance <= Fixed64.Zero,
            nameof(portalHeightClearance),
            "Portal height clearance must be positive.");
        SwiftThrowHelper.ThrowIfArgument(
            additionalCost < Fixed64.Zero,
            nameof(additionalCost),
            "Additional cost must be non-negative.");
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(destination.MapId),
            nameof(destination),
            "Destination must contain a valid map ID.");

        Id = id;
        SourceIndex = sourceIndex;
        Destination = destination;
        EntryAnchor = entryAnchor;
        ExitAnchor = exitAnchor;
        PortalRadiusClearance = portalRadiusClearance;
        PortalHeightClearance = portalHeightClearance;
        AdditionalCost = additionalCost;
        IsLowerBoundCertified = isLowerBoundCertified;
        _witnesses = CopyWitnesses(witnesses);
        for (int i = 0; i < _witnesses.Length; i++)
        {
            SwiftThrowHelper.ThrowIfArgument(
                string.IsNullOrWhiteSpace(_witnesses[i].MapId),
                nameof(witnesses),
                "Every witness must contain a valid map ID.");
        }
        _witnessView = Array.AsReadOnly(_witnesses);
    }

    /// <inheritdoc/>
    public bool Equals(NavigationConnection? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || !string.Equals(Id, other.Id, StringComparison.Ordinal)
            || !SourceIndex.Equals(other.SourceIndex)
            || !Destination.Equals(other.Destination)
            || EntryAnchor != other.EntryAnchor
            || ExitAnchor != other.ExitAnchor
            || PortalRadiusClearance != other.PortalRadiusClearance
            || PortalHeightClearance != other.PortalHeightClearance
            || AdditionalCost != other.AdditionalCost
            || IsLowerBoundCertified != other.IsLowerBoundCertified
            || _witnesses.Length != other._witnesses.Length)
        {
            return false;
        }

        for (int i = 0; i < _witnesses.Length; i++)
        {
            if (!_witnesses[i].Equals(other._witnesses[i]))
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as NavigationConnection);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int idHash = SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(Id);
        int hash = SwiftHashTools.CombineHashCodes(idHash, SourceIndex.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, Destination.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, EntryAnchor.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, ExitAnchor.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, PortalRadiusClearance.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, PortalHeightClearance.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, AdditionalCost.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, IsLowerBoundCertified ? 1 : 0);
        for (int i = 0; i < _witnesses.Length; i++)
            hash = SwiftHashTools.CombineHashCodes(hash, _witnesses[i].GetHashCode());
        return hash;
    }

    private static NavigationCellAddress[] CopyWitnesses(
        IEnumerable<NavigationCellAddress>? witnesses)
    {
        if (witnesses == null)
            return Array.Empty<NavigationCellAddress>();

        if (witnesses is ICollection<NavigationCellAddress> collection)
        {
            var copy = new NavigationCellAddress[collection.Count];
            collection.CopyTo(copy, 0);
            return copy;
        }

        var values = new SwiftCollections.SwiftList<NavigationCellAddress>();
        foreach (NavigationCellAddress witness in witnesses)
            values.Add(witness);
        return values.ToArray();
    }
}
