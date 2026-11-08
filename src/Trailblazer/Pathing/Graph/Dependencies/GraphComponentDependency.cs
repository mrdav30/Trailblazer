//=======================================================================
// GraphComponentDependency.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Identifies one structural component generation.</summary>
internal readonly struct GraphComponentDependency : IEquatable<GraphComponentDependency>
{
    internal GraphComponentDependency(NavigationSurfaceComponentKey key, long version)
    {
        Key = key;
        Version = version;
    }

    internal NavigationSurfaceComponentKey Key { get; }

    internal long Version { get; }

    public bool Equals(GraphComponentDependency other) =>
        Key.Equals(other.Key)
        && Version == other.Version;

    public override int GetHashCode()
    {
        return SwiftHashTools.CombineHashCodes(Key.GetHashCode(), Version.GetHashCode());
    }
}
