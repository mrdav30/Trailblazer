//=======================================================================
// NavigationMediumSlots.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Stores at most one value for each exact known traversal medium.</summary>
internal readonly struct NavigationMediumSlots<T>
{
    private readonly T _solid;
    private readonly T _gas;
    private readonly T _liquid;
    private readonly byte _mask;

    private NavigationMediumSlots(T solid, T gas, T liquid, byte mask)
    {
        _solid = solid;
        _gas = gas;
        _liquid = liquid;
        _mask = mask;
    }

    internal bool TryGet(TraversalMedium medium, out T value)
    {
        byte bit = GetBit(medium);
        if ((_mask & bit) == 0)
        {
            value = default!;
            return false;
        }
        value = medium == TraversalMedium.Solid
            ? _solid
            : medium == TraversalMedium.Gas
                ? _gas
                : _liquid;
        return true;
    }

    internal NavigationMediumSlots<T> Set(TraversalMedium medium, T value)
    {
        byte bit = GetBit(medium);
        return medium == TraversalMedium.Solid
            ? new NavigationMediumSlots<T>(value, _gas, _liquid, (byte)(_mask | bit))
            : medium == TraversalMedium.Gas
                ? new NavigationMediumSlots<T>(_solid, value, _liquid, (byte)(_mask | bit))
                : new NavigationMediumSlots<T>(_solid, _gas, value, (byte)(_mask | bit));
    }

    internal NavigationMediumSlots<T> Remove(TraversalMedium medium)
    {
        byte bit = GetBit(medium);
        return medium == TraversalMedium.Solid
            ? new NavigationMediumSlots<T>(default!, _gas, _liquid, (byte)(_mask & ~bit))
            : medium == TraversalMedium.Gas
                ? new NavigationMediumSlots<T>(_solid, default!, _liquid, (byte)(_mask & ~bit))
                : new NavigationMediumSlots<T>(_solid, _gas, default!, (byte)(_mask & ~bit));
    }

    internal bool IsEmpty => _mask == 0;

    internal static byte GetBit(TraversalMedium medium) => medium switch
    {
        TraversalMedium.Solid => 1,
        TraversalMedium.Gas => 2,
        TraversalMedium.Liquid => 4,
        _ => 0
    };
}
