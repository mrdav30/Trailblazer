//=======================================================================
// NavigatorGlobalIdAllocatorState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;

namespace Trailblazer.Navigation;

/// <summary>
/// Allocates deterministic navigator ids for one Trailblazer world context.
/// </summary>
internal sealed class NavigatorGlobalIdAllocatorState
{
    private long _nextId;

    internal Guid Create()
    {
        long next = Interlocked.Increment(ref _nextId);
        return new Guid(
            unchecked((int)next),
            unchecked((short)(next >> 32)),
            unchecked((short)(next >> 48)),
            (byte)'T',
            (byte)'R',
            (byte)'A',
            (byte)'I',
            (byte)'L',
            (byte)'B',
            (byte)'L',
            (byte)'Z');
    }

    internal void Reset()
    {
        Interlocked.Exchange(ref _nextId, 0);
    }
}
