//=======================================================================
// NavigationByteCount.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

internal static class NavigationByteCount
{
    internal static long SaturatingAdd(long left, long right) =>
        (long)Math.Min((ulong)left + (ulong)right, (ulong)long.MaxValue);
}
