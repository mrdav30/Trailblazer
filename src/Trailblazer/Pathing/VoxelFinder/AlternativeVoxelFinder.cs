using FixedMathSharp;
using GridForge.Grids;
using System.Diagnostics;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Pathfinding queries require 2 valid voxels. 
    /// When one is not valid, this is used to find the best nearest voxel to path to instead.
    /// </summary>
    public class AlternativeVoxelFinder
    {
        public static AlternativeVoxelFinder Instance = new();

        private Vector3d _worldPos;

        private Vector3d _offsettedPos;

        private int _maxTestDistance;

        private (int x, int y, int z) _direction;

        private int _layer;

        private Fixed64 _closestDistance;

        public void SetQuery(Vector3d worldPos, Vector3d offset, int maxTestDistance)
        {
            _worldPos = worldPos;
            _offsettedPos = offset;

            _maxTestDistance = maxTestDistance;

            _closestDistance = Fixed64.MAX_VALUE;
            _layer = 1;
        }

        public bool GetVoxel(out Voxel nextVoxel)
        {
            // Calculated closest side to raycast in first
            Fixed64 xDif = _offsettedPos.x - _worldPos.x;
            xDif = xDif.ClampOne();

            Fixed64 zDif = _offsettedPos.z - _worldPos.z;
            zDif = zDif.ClampOne();

            // Check to see if we should raycast towards corner first
            if ((xDif.Abs() >= GlobalGridManager.VoxelResolution) && (zDif.Abs() >= GlobalGridManager.VoxelResolution))
            {
                _direction.x = xDif.CeilToInt();
                _direction.z = zDif.CeilToInt();
            }
            else
            {
                if (xDif.Abs() < zDif.Abs())
                {
                    _direction.x = 0;
                    _direction.z = zDif.CeilToInt();
                }
                else
                {
                    _direction.x = xDif.CeilToInt();
                    _direction.z = 0;
                }
            }

            int layerStartX = _direction.x,
                layerStartZ = _direction.z;

            int iterations = 0; // <- this is for debugging
            for (_layer = 1; _layer <= _maxTestDistance;)
            {
                Vector3d checkPosition = new(
                    _worldPos.x + _direction.x,
                    Fixed64.Zero,
                    _worldPos.z + _direction.z);
                if (GlobalGridManager.TryGetVoxel(checkPosition, out Voxel checkVoxel))
                {
                    if (CheckPathVoxel(checkVoxel, out nextVoxel))
                        return true;
                }

                AdvanceRotation();
                // If we make a full loop
                if (layerStartX == _direction.x && layerStartZ == _direction.y)
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
                    Debug.WriteLine("too many");
                    break;
                }
            }

            nextVoxel = null;
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

        private bool CheckPathVoxel(Voxel voxel, out Voxel closestVoxel)
        {
            closestVoxel = null;
            if (voxel == null || voxel.IsBlocked)
                return false;

            Fixed64 distance = Vector3d.SqrDistance(voxel.WorldPosition, _worldPos);
            if (distance < _closestDistance)
            {
                _closestDistance = distance;
                closestVoxel = voxel;

                return true;
            }

            return false;
        }
    }
}
