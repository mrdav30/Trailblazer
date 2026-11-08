//=======================================================================
// NavigationFlowQueryResult.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Owns one successful flow payload lease and its exact resolved origin.</summary>
internal readonly struct NavigationFlowQueryResult : IDisposable
{
    internal NavigationFlowQueryResult(
        NavigationCellAddress resolvedOrigin,
        NavigationFlowFieldPayloadLease payloadLease)
    {
        ResolvedOrigin = resolvedOrigin;
        PayloadLease = payloadLease;
    }

    internal NavigationCellAddress ResolvedOrigin { get; }

    internal NavigationFlowFieldPayloadLease PayloadLease { get; }

    public void Dispose() => PayloadLease.Dispose();
}
