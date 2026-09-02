//=======================================================================
// NavigationGenerationCounter.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Advances bounded identities without allowing an alias-producing wrap.</summary>
internal static class NavigationGenerationCounter
{
    internal static bool CanAdvance(long generation) => generation < long.MaxValue;

    internal static bool CanAdvance(ulong generation) => generation < ulong.MaxValue;

    internal static long Advance(long generation, string exhaustionMessage)
    {
        if (!CanAdvance(generation))
            throw new InvalidOperationException(exhaustionMessage);
        return generation + 1;
    }

    internal static ulong Advance(ulong generation, string exhaustionMessage)
    {
        if (!CanAdvance(generation))
            throw new InvalidOperationException(exhaustionMessage);
        return generation + 1;
    }
}
