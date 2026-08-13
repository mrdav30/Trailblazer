//=======================================================================
// PathRequestHashBuilder.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Deterministic allocation-free hash combiner for path request cache keys.
/// </summary>
internal struct PathRequestHashBuilder
{
    private const int Seed = 5381;
    private const int Shift1 = 16;
    private const int Shift2 = 5;
    private const int Shift3 = 27;
    private const int Factor3 = 1566083941;

    private int _hash1;
    private int _hash2;
    private int _count;

    private PathRequestHashBuilder(int hash1, int hash2)
    {
        _hash1 = hash1;
        _hash2 = hash2;
        _count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PathRequestHashBuilder Create()
    {
        int hash = (Seed << Shift1) + Seed;
        return new PathRequestHashBuilder(hash, hash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(int value)
    {
        if ((_count & 1) == 0)
            _hash1 = Mix(_hash1, value);
        else
            _hash2 = Mix(_hash2, value);

        _count++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(bool value)
    {
        Add(value ? 1 : 0);
    }

    public void AddOrdinalString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Add(0);
            return;
        }

        unchecked
        {
            int hash = 17;
            for (int i = 0; i < value.Length; i++)
                hash = (hash * 31) + value[i];

            Add(hash);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ToHashCode()
    {
        unchecked
        {
            return _hash1 + (_hash2 * Factor3);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Mix(int hash, int itemHash)
    {
        unchecked
        {
            return ((hash << Shift2) + hash + (hash >> Shift3)) ^ itemHash;
        }
    }
}
