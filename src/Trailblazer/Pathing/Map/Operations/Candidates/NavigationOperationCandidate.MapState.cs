//=======================================================================
// NavigationOperationCandidate.MapState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Contains the MapState companion type.</content>
internal sealed partial class NavigationOperationCandidate
{
    /// <summary>Retains the staged replacement and original state for one map.</summary>
    internal sealed class MapState
    {
        internal MapState(
            NavigationMap map,
            long bakeVersion,
            long preparedMapRetainedBytes,
            NavigationMapOverlayState overlay,
            long dynamicSlotGeneration,
            PersistentVoxelIndexMap<byte>? dynamicAddresses = null,
            NavigationBakedCellLookup? bakedCellLookup = null)
        {
            Map = map;
            BakeVersion = bakeVersion;
            PreparedMapRetainedBytes = preparedMapRetainedBytes;
            Overlay = overlay;
            DynamicSlotGeneration = dynamicSlotGeneration;
            DynamicAddresses = dynamicAddresses ?? PersistentVoxelIndexMap<byte>.Empty;
            BakedCellLookup = bakedCellLookup ?? NavigationBakedCellLookup.Create(map);
        }

        internal NavigationMap Map { get; }

        internal long BakeVersion { get; }

        internal long PreparedMapRetainedBytes { get; }

        internal NavigationMapOverlayState Overlay { get; }

        internal long DynamicSlotGeneration { get; }

        internal PersistentVoxelIndexMap<byte> DynamicAddresses { get; }

        internal NavigationBakedCellLookup BakedCellLookup { get; }
    }
}
