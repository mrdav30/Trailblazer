//=======================================================================
// NavigationSeamEditToken.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>Provides process-unique nonwrapping ownership for seam edit sessions.</summary>
internal readonly struct NavigationSeamEditToken
{
    private static long _highWater;

    private NavigationSeamEditToken(long value) => Value = value;

    internal long Value { get; }

    internal static NavigationSeamEditToken Create()
    {
        long value = Interlocked.Increment(ref _highWater);
        if (value <= 0)
            throw new InvalidOperationException("The seam edit ownership token space was exhausted.");
        return new NavigationSeamEditToken(value);
    }
}
