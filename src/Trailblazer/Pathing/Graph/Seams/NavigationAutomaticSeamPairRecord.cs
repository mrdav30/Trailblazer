//=======================================================================
// NavigationAutomaticSeamPairRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Stores snapshot-local active state without duplicating canonical portal geometry.</summary>
internal sealed class NavigationAutomaticSeamPairRecord
{
    internal const long RetainedSize = 32L;

    internal NavigationAutomaticSeamPairRecord(
        NavigationAutomaticSeamPair pair,
        bool isActive)
    {
        Pair = pair;
        IsActive = isActive;
    }

    internal NavigationAutomaticSeamPair Pair { get; }

    internal bool IsActive { get; }
}
