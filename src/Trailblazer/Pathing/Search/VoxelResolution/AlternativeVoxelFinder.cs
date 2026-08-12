using FixedMathSharp;
using GridForge.Grids;
using System.Diagnostics.CodeAnalysis;

namespace Trailblazer.Pathing;

/// <summary>
/// Performs a bounded same-layer fallback scan around a query position and returns
/// the first unblocked voxel found in deterministic star/ring order.
/// </summary>
public class AlternativeVoxelFinder
{
    private Vector3d _worldPos;

    private Vector3d _anchorVoxelPosition;

    private GridWorld? _world;

    private Fixed64 _voxelSize;

    private int _maxTestDistance;

    private (int x, int z) _direction;

    private int _layer;

    /// <summary>
    /// Configures the fallback search around the given world-space query point for one explicit context.
    /// </summary>
    public void SetQuery(
        TrailblazerWorldContext context,
        Vector3d worldPos,
        Voxel anchorVoxel,
        int maxTestDistance)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        _world = context.World;
        _voxelSize = context.VoxelSize;
        _worldPos = worldPos;
        _anchorVoxelPosition = anchorVoxel.WorldPosition;
        _maxTestDistance = maxTestDistance;
        _layer = 1;
    }

    /// <summary>
    /// Attempts to find the first unblocked voxel in deterministic star/ring order on the query layer.
    /// </summary>
    public bool GetVoxel([MaybeNullWhen(false)] out Voxel nextVoxel)
    {
        nextVoxel = null;
        InitializeDirection();

        int layerStartX = _direction.x;
        int layerStartZ = _direction.z;

        int iterations = 0; // <- this is for debugging
        for (_layer = 1; _layer <= _maxTestDistance;)
        {
            Vector3d checkPosition = new(
                _worldPos.X + _direction.x,
                _worldPos.Y,
                _worldPos.Z + _direction.z);
            if (_world != null
                && _world.TryGetVoxel(checkPosition, out Voxel? checkVoxel)
                && checkVoxel != null
                && IsSearchCandidate(checkVoxel))
            {
                nextVoxel = checkVoxel;
                return true;
            }

            AdvanceRotation();
            // If we make a full loop
            if (layerStartX == _direction.x && layerStartZ == _direction.z)
            {
                _layer++;
                // Advance a layer instead of rotation
                if (_direction.x > 0)
                    _direction.x = _layer;
                else if (_direction.x < 0)
                    _direction.x = -_layer;

                if (_direction.z > 0)
                    _direction.z = _layer;
                else if (_direction.z < 0)
                    _direction.z = -_layer;

                layerStartX = _direction.x;
                layerStartZ = _direction.z;
            }

            iterations++;
            if (iterations > 500)
            {
                TrailblazerLogger.Channel.Error(
                    $"Alternative voxel search exceeded the iteration safety cap for query {_worldPos} at layer {_layer} with max test distance {_maxTestDistance}.");
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Advances the rotation clockwise
    /// </summary>
    private void AdvanceRotation()
    {
        // sides
        if (_direction.x == 0)
        {
            if (_direction.z == 1)  // up
                _direction.x = _layer;
            else  // down
                _direction.x = -_layer;

            return;
        }

        if (_direction.z == 0)
        {

            if (_direction.x == 1)  // right
                _direction.z = -_layer;
            else  // left
                _direction.z = _layer;

            return;
        }

        // corners
        if (_direction.x > 0)
        {

            if (_direction.z > 0)  // top-right
                _direction.z = 0;
            else
                _direction.x = 0;  // bottom-right

            return;
        }

        if (_direction.z > 0)  // top-left
            _direction.x = 0;
        else
            _direction.z = 0;  // bottom-left
    }

    private static bool IsSearchCandidate(Voxel voxel) =>
        voxel != null
        && !voxel.IsBlocked;

    private void InitializeDirection()
    {
        Fixed64 halfVoxel = _voxelSize * Fixed64.Half;
        Fixed64 xOffsetFromCenter = _worldPos.X - (_anchorVoxelPosition.X + halfVoxel);
        Fixed64 zOffsetFromCenter = _worldPos.Z - (_anchorVoxelPosition.Z + halfVoxel);

        if (xOffsetFromCenter.Abs() >= zOffsetFromCenter.Abs())
        {
            _direction.x = xOffsetFromCenter < Fixed64.Zero ? -1 : 1;
            _direction.z = 0;
            return;
        }

        _direction.x = 0;
        _direction.z = zOffsetFromCenter < Fixed64.Zero ? -1 : 1;
    }
}
