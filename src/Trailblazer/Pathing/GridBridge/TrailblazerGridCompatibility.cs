//=======================================================================
// TrailblazerGridCompatibility.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines the GridForge world model supported by Trailblazer's current pathing algorithms.
/// </summary>
/// <remarks>
/// Topology-aware pathing is intentionally isolated behind this policy so a later implementation can
/// replace the dense cubic assumptions without scattering compatibility checks through the surveyors.
/// </remarks>
internal static class TrailblazerGridCompatibility
{
    internal static Fixed64 GetRepresentativeCellEdge(GridWorld world)
    {
        ValidateWorld(world);
        return GridTopologyMetricUtility.GetRepresentativeCellEdge(world);
    }

    internal static void ValidateWorld(GridWorld world)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        Fixed64? worldCellEdge = null;
        foreach (VoxelGrid grid in world.ActiveGrids)
        {
            if (!grid.IsActive)
                continue;

            if (grid.Configuration.TopologyKind != GridTopologyKind.RectangularPrism)
            {
                throw new NotSupportedException(
                    "Trailblazer pathing does not yet support hex topology. " +
                    "Topology-aware pathfinding is planned as a fast-follow redesign.");
            }

            if (grid.StorageKind != GridStorageKind.Dense)
            {
                throw new NotSupportedException(
                    "Trailblazer pathing does not yet support sparse grid storage. " +
                    "Sparse pathfinding is planned as a fast-follow redesign.");
            }

            GridTopologyMetrics metrics = grid.Configuration.TopologyMetrics;
            if (metrics.CellWidth != metrics.LayerHeight
                || metrics.CellWidth != metrics.CellLength)
            {
                throw new NotSupportedException(
                    "Trailblazer pathing does not yet support anisotropic rectangular cells. " +
                    "Topology-aware pathfinding is planned as a fast-follow redesign.");
            }

            Fixed64 gridCellEdge = metrics.CellWidth;
            if (worldCellEdge.HasValue && worldCellEdge.Value != gridCellEdge)
            {
                throw new NotSupportedException(
                    "Trailblazer pathing does not yet support conflicting cell metrics across active grids. " +
                    "Mixed-metric pathfinding is planned as a fast-follow redesign.");
            }

            worldCellEdge = gridCellEdge;
        }
    }

    internal static void ValidateWorld(GridWorld world, Fixed64 requiredCellEdge, string parameterName)
    {
        Fixed64 worldCellEdge = GetRepresentativeCellEdge(world);
        if (worldCellEdge == requiredCellEdge)
            return;

        throw new ArgumentException(
            $"The requested cell edge {requiredCellEdge} conflicts with the owning world's supported cell edge {worldCellEdge}.",
            parameterName);
    }
}
