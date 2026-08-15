//=======================================================================
// NavigationSurfaceComponent.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Stores one immutable exact weak surface component.</summary>
internal sealed class NavigationSurfaceComponent
{
    internal NavigationSurfaceComponent(
        NavigationSurfaceComponentKey key,
        long version,
        NavigationPagedSequence<NavigationCellAddress> members,
        bool allSurfaceEdgesEuclideanCertified)
    {
        Key = key;
        Version = version;
        Members = members;
        AllSurfaceEdgesEuclideanCertified = allSurfaceEdgesEuclideanCertified;
    }

    internal NavigationSurfaceComponentKey Key { get; }

    internal long Version { get; }

    internal NavigationPagedSequence<NavigationCellAddress> Members { get; }

    internal bool AllSurfaceEdgesEuclideanCertified { get; }

    internal long RetainedBytes => checked(48L + Members.RetainedBytes);

    internal int PersistentPageCount => checked(1 + Members.PersistentPageCount);
}
