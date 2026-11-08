//=======================================================================
// NavigationSurfaceComponentKeySet.Enumerator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Contains the Enumerator companion type.</content>
internal sealed partial class NavigationSurfaceComponentKeySet
{
    /// <summary>Traverses the surface component keys in deterministic order.</summary>
    internal struct Enumerator
    {
        private readonly NavigationSurfaceComponentKeySet _set;
        private int _map;
        private int _address;
        private TraversalMedium _medium;
        private TraversalMedium _currentMedium;

        internal Enumerator(NavigationSurfaceComponentKeySet set)
        {
            _set = set;
            _map = 0;
            _address = 0;
            _medium = TraversalMedium.Solid;
            _currentMedium = TraversalMedium.Unknown;
        }

        internal NavigationSurfaceComponentKey Current
        {
            get
            {
                PersistentVoxelIndexMap<byte> values = _set._maps.GetValueAt(_map);
                return new NavigationSurfaceComponentKey(
                    new NavigationCellAddress(
                        _set._maps.GetKeyAt(_map),
                        values.GetKeyAt(_address)),
                    _currentMedium);
            }
        }

        internal bool MoveNext()
        {
            while (_map < _set._maps.Count)
            {
                PersistentVoxelIndexMap<byte> values = _set._maps.GetValueAt(_map);
                while (_address < values.Count)
                {
                    byte mask = values.GetValueAt(_address);
                    while (_medium <= TraversalMedium.Liquid)
                    {
                        TraversalMedium medium = _medium++;
                        if ((mask & NavigationMediumSlots<byte>.GetBit(medium)) == 0)
                            continue;
                        _currentMedium = medium;
                        return true;
                    }
                    _address++;
                    _medium = TraversalMedium.Solid;
                }
                _map++;
                _address = 0;
            }
            return false;
        }
    }
}
