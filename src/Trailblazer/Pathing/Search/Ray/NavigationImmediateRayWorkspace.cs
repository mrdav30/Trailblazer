//=======================================================================
// NavigationImmediateRayWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Serializes synchronous callers over one fixed navigation-ray workspace.</summary>
internal sealed class NavigationImmediateRayWorkspace
{
    internal NavigationImmediateRayWorkspace(
        int mapCapacity,
        int pageCapacity,
        int componentCapacity,
        int coveredAddressCapacity,
        int traceIntervalCapacity)
    {
        SyncRoot = new object();
        Workspace = new NavigationRayWorkspace(
            mapCapacity,
            pageCapacity,
            componentCapacity,
            coveredAddressCapacity,
            traceIntervalCapacity);
    }

    internal object SyncRoot { get; }

    internal NavigationRayWorkspace Workspace { get; }
}
