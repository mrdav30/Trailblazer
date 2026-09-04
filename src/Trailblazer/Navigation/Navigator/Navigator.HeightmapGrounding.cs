//=======================================================================
// Navigator.HeightmapGrounding.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using Trailblazer.Heightmaps;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Navigation;

public abstract partial class Navigator
{
    #region Heightmap Grounding

    /// <summary>
    /// Applies configured heightmap grounding when a concrete navigator chooses to opt in.
    /// </summary>
    /// <remarks>
    /// The base navigator never calls this automatically. Host/concrete navigators should invoke it
    /// from their traversal probing code after they know the navigator is grounded on solid terrain.
    /// </remarks>
    /// <param name="updateMotorState">Whether to immediately sync the resulting ground contact into the motor.</param>
    /// <param name="surfaceFriction">Optional friction stored in the generated ground condition.</param>
    /// <param name="motionTransfer">Motion transfer mode stored in the generated ground condition.</param>
    /// <returns>True when a registered heightmap sample updated this navigator's ground contact.</returns>
    protected bool TryApplyHeightmapGrounding(
        bool updateMotorState = false,
        Fixed64? surfaceFriction = null,
        MotionTransfer motionTransfer = MotionTransfer.None)
    {
        if (!IsActive || _heightmapGrounding.Mode == HeightmapGroundingMode.Disabled)
            return false;
        if (_frameCondition.Medium != TraversalMedium.Solid)
            return false;

        TrailblazerWorldContext context = RequireContext();
        Vector3d queryPosition = GetHeightmapGroundingQueryPosition();
        string? preferredLayerName = _heightmapGrounding.ActiveLayerName ?? _heightmapGrounding.LayerName;
        if (!context.Heightmaps.TrySampleGround(queryPosition, preferredLayerName, out HeightmapSample sample))
        {
            _heightmapGrounding.ActiveLayerName = null;
            return false;
        }

        _heightmapGrounding.ActiveLayerName = sample.LayerName;
        SetGroundContact(
            surfaceLevel: sample.GroundY,
            surfaceNormal: Vector3d.Up,
            surfaceFriction: surfaceFriction,
            motionTransfer: motionTransfer,
            updateMotorState: updateMotorState);

        if (_heightmapGrounding.Mode == HeightmapGroundingMode.SurfaceLevelAndPosition)
            TryProjectRootToHeightmapSample(sample);

        return true;
    }

    private Vector3d GetHeightmapGroundingQueryPosition()
    {
        return new Vector3d(
            _position.X,
            _position.Y - BodyShape.RootToFootOffsetY - _heightmapGrounding.GroundOffset,
            _position.Z);
    }

    private void TryProjectRootToHeightmapSample(HeightmapSample sample)
    {
        Fixed64 targetRootY = sample.GroundY + BodyShape.RootToFootOffsetY + _heightmapGrounding.GroundOffset;
        Fixed64 correctionY = targetRootY - _position.Y;
        if (correctionY == Fixed64.Zero)
            return;

        Fixed64? snapTolerance = _heightmapGrounding.SnapTolerance;
        if (snapTolerance.HasValue && correctionY.Abs() > snapTolerance.Value)
            return;

        ProjectRootWithoutVelocity(new Vector3d(Fixed64.Zero, correctionY, Fixed64.Zero));
    }

    private void ProjectRootWithoutVelocity(Vector3d correction)
    {
        Vector3d oldPosition = _position;
        _position += correction;
        _lastPosition += correction;
        UpdateVoxelOccupancyAfterRootProjection(oldPosition, _position);
    }

    private void UpdateVoxelOccupancyAfterRootProjection(Vector3d oldPosition, Vector3d newPosition)
    {
        NavigatorOccupancyTracker.UpdateAfterRootProjection(
            _context!.World, this, oldPosition, newPosition);
    }

    #endregion
}
