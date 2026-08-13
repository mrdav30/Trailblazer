//=======================================================================
// NavigationResultEntryLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>Owns one checked-out immutable context result entry.</summary>
internal sealed class NavigationResultEntryLease<TPayload> : IDisposable where TPayload : class
{
    private NavigationContextResultCache<TPayload>? _owner;
    private NavigationResultCacheEntry<TPayload>? _entry;

    internal NavigationResultEntryLease(
        NavigationContextResultCache<TPayload> owner,
        NavigationResultCacheEntry<TPayload> entry)
    {
        Reinitialize(owner, entry);
    }

    internal NavigationContextResultCache<TPayload>? Owner => Volatile.Read(ref _owner);

    internal NavigationResultCacheEntry<TPayload>? Entry => Volatile.Read(ref _entry);

    internal TPayload Payload => Volatile.Read(ref _entry)!.Payload;

    internal void Reinitialize(
        NavigationContextResultCache<TPayload> owner,
        NavigationResultCacheEntry<TPayload> entry)
    {
        _entry = entry;
        Volatile.Write(ref _owner, owner);
    }

    internal void Rebind(NavigationResultCacheEntry<TPayload> entry) =>
        Volatile.Write(ref _entry, entry);

    internal NavigationResultCacheEntry<TPayload> Detach() =>
        Interlocked.Exchange(ref _entry, null)!;

    public void Dispose()
    {
        NavigationContextResultCache<TPayload>? owner = Interlocked.Exchange(ref _owner, null);
        owner?.Return(this);
    }
}
