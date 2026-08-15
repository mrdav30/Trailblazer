//=======================================================================
// NavigationFlowFieldPayloadLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Generation-validates one active reference to an immutable flow payload.</summary>
internal readonly struct NavigationFlowFieldPayloadLease : IDisposable
{
    private readonly NavigationFlowFieldPayloadCache? _owner;
    private readonly int _slot;
    private readonly ulong _generation;

    internal NavigationFlowFieldPayloadLease(
        NavigationFlowFieldPayloadCache owner,
        int slot,
        ulong generation)
    {
        _owner = owner;
        _slot = slot;
        _generation = generation;
    }

    internal NavigationFlowFieldStatus TryGetPayload(
        out NavigationFlowFieldPayload payload) => _owner == null
        ? ReturnStale(out payload)
        : _owner.TryGetPayload(_slot, _generation, out payload);

    public void Dispose() => _owner?.Return(_slot, _generation);

    private static NavigationFlowFieldStatus ReturnStale(
        out NavigationFlowFieldPayload payload)
    {
        payload = null!;
        return NavigationFlowFieldStatus.Stale;
    }
}
