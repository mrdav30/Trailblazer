//=======================================================================
// NavigationAStarPayloadLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>Owns one active reference to an immutable A* payload.</summary>
internal sealed class NavigationAStarPayloadLease : IDisposable
{
    private readonly NavigationAStarPayloadCache _owner;
    private NavigationAStarPayloadCache.CacheEntry? _entry;

    internal NavigationAStarPayloadLease(NavigationAStarPayloadCache owner)
    {
        _owner = owner;
    }

    internal NavigationAStarPayload Payload => _entry?.Payload
        ?? throw new ObjectDisposedException(nameof(NavigationAStarPayloadLease));

    internal NavigationAStarPayloadLease? NextPooled { get; set; }

    internal void Bind(NavigationAStarPayloadCache.CacheEntry entry)
    {
        if (_entry != null)
            throw new InvalidOperationException("The A* payload lease is already active.");
        _entry = entry;
    }

    internal NavigationAStarPayloadCache.CacheEntry? DetachEntry() =>
        Interlocked.Exchange(ref _entry, null);

    public void Dispose() => _owner.Return(this);
}
