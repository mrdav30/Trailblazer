using SwiftCollections;
using FixedMathSharp;
using UnityEngine;
using SwiftCollections.Dimensions;

namespace Lockstep.Environment
{
    /// <summary>
    /// Stores a height map for the environment based on x & z (y for 2d) coordinates
    /// </summary>
    [System.Serializable]
    public class HeightMap
    {
        [SerializeField]
        private string _name;
        public string Name { get { return _name; } }

        [SerializeField, HideInInspector]
        private ShortArray2D _map;
        public ShortArray2D Map
        {
            get => _map;
            set => _map = value;
        }

        [SerializeField]
        private LayerMask _scanLayers;
        public LayerMask ScanLayers { get { return _scanLayers; } }

        public HeightMap(short[,] map)
        {
            _map = new ShortArray2D(map);
        }

        public Fixed64 GetHeight(int gridX, int gridY)
        {
            if (!_map.IsValidIndex(gridX, gridY))
                return Fixed64.Zero;

            return HeightMapSaver.Uncompress(_map[gridX, gridY]);
        }
    }
}