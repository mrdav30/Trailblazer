//=======================================================================
// NavigationNodeRef.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Identifies one stable cell slot within one immutable graph root.</summary>
internal readonly struct NavigationNodeRef : IEquatable<NavigationNodeRef>
{
    private readonly int _encodedMapOrdinal;
    private readonly int _encodedCellSlot;

    internal NavigationNodeRef(int mapOrdinal, int cellSlot)
    {
        if (mapOrdinal < 0
            || mapOrdinal == int.MaxValue
            || cellSlot < 0
            || cellSlot == int.MaxValue)
        {
            _encodedMapOrdinal = 0;
            _encodedCellSlot = 0;
            return;
        }

        _encodedMapOrdinal = mapOrdinal + 1;
        _encodedCellSlot = cellSlot + 1;
    }

    internal int MapOrdinal => _encodedMapOrdinal - 1;

    internal int CellSlot => _encodedCellSlot - 1;

    internal bool IsValid => _encodedMapOrdinal > 0 && _encodedCellSlot > 0;

    public bool Equals(NavigationNodeRef other) =>
        _encodedMapOrdinal == other._encodedMapOrdinal
        && _encodedCellSlot == other._encodedCellSlot;

    public override int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(_encodedMapOrdinal, _encodedCellSlot);

}
