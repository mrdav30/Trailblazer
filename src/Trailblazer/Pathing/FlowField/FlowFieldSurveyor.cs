using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System.Threading;
using System;
using SwiftCollections.Pool;
using System.Collections.Concurrent;
using System.Linq;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Responsible for generating flow fields using a wavefront flood-fill algorithm.
    /// Provides pathfinding data suitable for many agents with shared destinations.
    /// </summary>
    public class FlowFieldSurveyor
    {
        #region Singleton Instances

        /// <summary>
        /// A lazily initialized singleton instance of the pathfinder.
        /// </summary>
        private static readonly Lazy<FlowFieldSurveyor> _instance =
            new(() => new FlowFieldSurveyor(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Gets the shared instance of the pathfinder.
        /// </summary>
        public static FlowFieldSurveyor Shared => _instance.Value;

        #endregion

        private readonly PathHeap _heap = new();

        /// <summary>
        /// Tracks partitions that were affected during the flood fill.
        /// These will be used to construct the final flow field result.
        /// </summary>
        private readonly SwiftHashSet<PathPartition> _marked = new();

        private readonly SwiftHashSet<string> _chartKeys = new();

        private FlowFieldPathRequest _request;

        private int _startDistanceMetric;

        /// <summary>
        /// Attempts to create a shared flow field path from the start to the end voxel specified in the request.
        /// </summary>
        /// <param name="request">A flow field path request containing the start, end, and search parameters.</param>
        /// <returns>A dictionary of flow fields indexed by spawn token.</returns>
        public FlowFieldSurveyResult FindPath(FlowFieldPathRequest request)
        {
            lock (SurveyorLock.GlobalLock)
            {
                if (!request.IsValid
                || request.HasZeroDisplacement
                || !request.End.TryGetPartition(out PathPartition targetPart))
                {
                    return FlowFieldSurveyResult.Empty;
                }

                _request = request;

                _heap.FastClear();
                _marked.Clear();
                _chartKeys.Clear();

                _startDistanceMetric = 0;

                // Start from the end and move towards the start voxel
                targetPart.PathCost = 0;
                _heap.Add(targetPart);
                _chartKeys.AddRange(targetPart.ChartOwners);

                if (!FloodPath() || _marked.Count <= 0)
                    return FlowFieldSurveyResult.Empty;

                SwiftDictionary<int, FlowField> flowFields = GenerateFlowFields();
                string[] chartsUsed = _chartKeys.ToArray();
                return FlowFieldSurveyResult.Create(flowFields, chartsUsed, request.RequestCacheKey);
            }
        }

        /// <summary>
        /// Executes the wavefront expansion (flood fill) phase of the flow field generation algorithm.
        /// Starts from the goal and expands outward until the start voxel is reached or search range is exceeded.
        /// </summary>
        /// <returns><c>true</c> if the start voxel is reached within the maximum range; otherwise <c>false</c>.</returns>
        private bool FloodPath()
        {
            bool targetReached = false;

            int iterations = 0;
            int searchSize = _request.MaxPathSearchRange.Value;
            int maxFloodRange = 0;

            while (_heap.RemoveFirst(out PathPartition current)
                && iterations++ < searchSize)
            {
                // Check if we found our way to the start voxel
                if (!targetReached)
                {
                    if (current.VoxelToken == _request.Start.SpawnToken)
                    {
                        _startDistanceMetric = current.PathCost;
                        maxFloodRange = current.PathCost + _request.ExtraFloodRange;
                        targetReached = true;
                    }

                }
                else if (current.PathCost >= maxFloodRange)
                    break;

                AnalyzeNeighborDistance(current, _request.UnitSize);

                _heap.SetClosed(current);
            }

            return targetReached;
        }

        /// <summary>
        /// Evaluates each walkable neighbor of the current partition and assigns a heap cost if a shorter path is found.
        /// Ensures the wavefront expands in an optimal order.
        /// </summary>
        /// <param name="current">The current path partition being evaluated.</param>
        /// <param name="unitSize">The size of the navigating agent.</param>
        private void AnalyzeNeighborDistance(PathPartition current, Fixed64 unitSize)
        {
            // Check each straight line neighbour of this voxel (no diagonals)
            // We will only ever visit every voxel once as we are always visiting voxels in the most efficient order
            foreach (LinearDirection dir in PathManager.PerpendicularDirections)
            {
                // pull the neighbor partition directly out of our baked neighbors[]
                PathPartition nPart = current.Neighbors[(int)dir];
                if (nPart is null)
                    continue;  // either out-of-bounds or blocked

                if (_heap.IsClosed(nPart) || nPart.IsImpassable(unitSize))
                    continue;

                int newCost = current.PathCost + 1;
                if (!_heap.Contains(nPart))
                {
                    nPart.PathCost = newCost;
                    _heap.Add(nPart);
                    _marked.Add(nPart);
                }
                else if (nPart.PathCost > newCost)
                {
                    nPart.PathCost = newCost;
                    _heap.SortUp(nPart);
                }
            }
        }

        /// <summary>
        /// Converts the results of the flood fill phase into directional flow fields pointing toward the goal.
        /// Each partition is assigned a direction vector blending shortest path and direct-to-goal direction.
        /// </summary>
        /// <returns>A dictionary of directional flow field data indexed by voxel spawn tokens.</returns>
        private SwiftDictionary<int, FlowField> GenerateFlowFields()
        {
            SwiftDictionary<int, FlowField> result = new(_marked.Count + 1)
            {
                // Ensure end voxel is include, it shouldn't point anywhere
                {
                    _request.End.SpawnToken,
                    new FlowField()
                    {
                        Direction = Vector3d.Zero,
                        GlobalIndex = _request.End.GlobalIndex,
                        IsGoal = true
                    }
                }
            };

            Fixed64 totalDistance = Fixed64.One + _startDistanceMetric; // + 1 for end part
            foreach (PathPartition current in _marked)
            {
                // end voxel shouldn't be marked, but just in case...
                if (current.VoxelToken == _request.End.SpawnToken)
                    continue;

                FlowField currentFlow = new()
                {
                    GlobalIndex = current.GlobalIndex,
                    PathCost = current.PathCostTotal
                };

                // Go through all neighbours and find the one with the lowest distance
                PathPartition minPartition = null;
                int minCost = int.MaxValue;
                for (int i = 0; i < current.Neighbors.Length; i++)
                {
                    PathPartition nPart = current.Neighbors[i];
                    // check closed heap version to ensure neighbor was part of flood phase
                    if (nPart == null || !_heap.IsClosed(nPart))
                        continue;

                    int dist = nPart.PathCostTotal - current.PathCost;
                    if (dist < minCost)
                    {
                        minPartition = nPart;
                        minCost = dist;
                    }
                }

                // If we found a valid neighbour, point in its direction by applying distance-weighted blending
                if (minPartition != null)
                {
                    Vector3d field = (minPartition.VoxelPosition - current.VoxelPosition).Normalize();
                    if (minCost == 1)
                        currentFlow.Direction = field;
                    else
                    {
                        // blend with the lowest-cost vector
                        Vector3d direct = (_request.End.WorldPosition - current.VoxelPosition).Normalize();
                        // closer = alpha → 1
                        Fixed64 alpha = FixedMath.Clamp01((totalDistance - current.PathCost) / totalDistance);
                        Vector3d blended = field * alpha + direct * (Fixed64.One - alpha);
                        currentFlow.Direction = blended.Normalize();
                    }
                }

                result.Add(current.VoxelToken, currentFlow);
                _chartKeys.AddRange(current.ChartOwners);
            }

            foreach (PathPartition part in _marked)
                part.PathCost = int.MaxValue;

            return result;
        }

        /// <summary>
        /// Samples an interpolated flow direction from a given world position using bilinear interpolation.
        /// Helps agents move smoothly between grid cells.
        /// </summary>
        /// <param name="worldPosition">The world-space position to sample from.</param>
        /// <param name="fields">A dictionary of flow field data.</param>
        /// <returns>An interpolated directional vector.</returns>
        public static Vector3d SampleFlowVector(Vector3d worldPosition, SwiftDictionary<int, FlowField> fields)
        {
            if (fields == null || fields.Count == 0)
                return Vector3d.Zero;

            // Get bottom-left corner of the square the agent is standing in
            Vector3d corner = new(
                FixedMath.Floor(worldPosition.x / GlobalGridManager.VoxelSize) * GlobalGridManager.VoxelSize,
                FixedMath.Floor(worldPosition.y / GlobalGridManager.VoxelSize) * GlobalGridManager.VoxelSize,
                FixedMath.Floor(worldPosition.z / GlobalGridManager.VoxelSize) * GlobalGridManager.VoxelSize
            );

            // Compute normalized offset in cell (0..1)
            Fixed64 dx = (worldPosition.x - corner.x) / GlobalGridManager.VoxelSize;
            Fixed64 dz = (worldPosition.z - corner.z) / GlobalGridManager.VoxelSize;

            // Sample the 4 surrounding voxel centers
            Vector3d bottomLeft = corner;
            Vector3d bottomRight = corner + new Vector3d(GlobalGridManager.VoxelSize, Fixed64.Zero, Fixed64.Zero);
            Vector3d topLeft = corner + new Vector3d(Fixed64.Zero, Fixed64.Zero, GlobalGridManager.VoxelSize);
            Vector3d topRight = corner + new Vector3d(GlobalGridManager.VoxelSize, Fixed64.Zero, GlobalGridManager.VoxelSize);

            // Get flow vectors
            Vector3d f00 = GetFlowDirection(bottomLeft, fields);
            Vector3d f10 = GetFlowDirection(bottomRight, fields);
            Vector3d f01 = GetFlowDirection(topLeft, fields);
            Vector3d f11 = GetFlowDirection(topRight, fields);

            // Bilinear interpolation
            Vector3d zHigh = f00 * (Fixed64.One - dx) + f10 * dx;
            Vector3d zLow = f01 * (Fixed64.One - dx) + f11 * dx;
            Vector3d blended = zHigh * (Fixed64.One - dz) + zLow * dz;

            blended.Normalize();
            return blended;
        }

        /// <summary>
        /// Attempts to locate the closest valid voxel from which to begin flow-based movement.
        /// Useful for finding an initial entry point to the flow field.
        /// </summary>
        /// <param name="origin">The world-space origin to search from.</param>
        /// <param name="fields">Flow field data indexed by voxel spawn token.</param>
        /// <param name="result">The closest valid voxel, if found.</param>
        /// <param name="range">Maximum range to search.</param>
        /// <returns><c>true</c> if a nearby flow field anchor is found; otherwise <c>false</c>.</returns>
        public static bool TryGetNearestFlowAnchor(
            Vector3d origin,
            SwiftDictionary<int, FlowField> fields,
            Fixed64 range,
            out Voxel result)
        {
            result = null;
            if (fields == null || fields.Count == 0)
                return false;

            Fixed64 minDistanceSq = range * range;
            bool found = false;

            foreach (FlowField flow in fields.Values)
            {
                if (!GlobalGridManager.TryGetGridAndVoxel(flow.GlobalIndex, out _, out Voxel flowVoxel))
                    continue;

                Fixed64 distSq = Vector3d.SqrDistance(origin, flowVoxel.WorldPosition);
                if (distSq <= minDistanceSq)
                {
                    result = flowVoxel;
                    minDistanceSq = distSq;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Retrieves the raw directional flow vector at the given world-space position, if available.
        /// </summary>
        /// <param name="position">The position to query within the flow field.</param>
        /// <param name="fields">Flow field data indexed by spawn token.</param>
        /// <returns>The direction vector, or <c>Vector3d.Zero</c> if no field exists.</returns>
        public static Vector3d GetFlowDirection(Vector3d position, SwiftDictionary<int, FlowField> fields)
        {
            if (GlobalGridManager.TryGetGridAndVoxel(position, out _, out Voxel voxel))
            {
                if (fields.TryGetValue(voxel.SpawnToken, out FlowField field))
                    return field.Direction;
            }
            return Vector3d.Zero;
        }

        public static FlowField GetFlowField(Vector3d position, SwiftDictionary<int, FlowField> fields)
        {
            if (GlobalGridManager.TryGetGridAndVoxel(position, out _, out Voxel voxel))
            {
                if (fields.TryGetValue(voxel.SpawnToken, out FlowField field))
                    return field;
            }
            return default;
        }
    }
}