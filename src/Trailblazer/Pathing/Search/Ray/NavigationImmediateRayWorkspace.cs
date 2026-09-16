//=======================================================================
// NavigationImmediateRayWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;

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
        var workspace = new NavigationRayWorkspace(
            mapCapacity,
            pageCapacity,
            componentCapacity,
            coveredAddressCapacity,
            traceIntervalCapacity);
        RayWork = new NavigationRayWork(workspace);
        WorkMeter = new NavigationWorkMeter(default);
        SourceOrdinals = new int[traceIntervalCapacity];
        ObserveSlab = slab => RayWork.ObserveDirectSlab(this, slab);
    }

    internal object SyncRoot { get; }

    internal NavigationRayWork RayWork { get; }

    internal NavigationWorkMeter WorkMeter { get; }

    internal int[] SourceOrdinals { get; }

    internal Func<GridTraceSlab, bool> ObserveSlab { get; }

    internal int UsedOrdinalCount;
    internal bool CertificateArmed;
    internal NavigationRayStatus DeferredStatus;

    internal void ClearDirect()
    {
        Array.Clear(SourceOrdinals, 0, UsedOrdinalCount);
        UsedOrdinalCount = 0;
        CertificateArmed = false;
        DeferredStatus = NavigationRayStatus.Pending;
    }
}
