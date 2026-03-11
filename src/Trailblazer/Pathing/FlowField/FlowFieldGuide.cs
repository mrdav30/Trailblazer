using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>
/// Provides steering direction based on a flow field vector grid.
/// Suitable for group-based or gradient-following movement strategies.
/// </summary>
public class FlowFieldGuide : IGuide
{
    public static readonly Fixed64 DefaultFieldSearchRange = new(10);

    public FlowFieldSurveyResult FlowMap { get; private set; }

    public bool Initialize(FlowFieldSurveyResult surveyResult)
    {
        if (!surveyResult.HasPath)
            return false;

        FlowMap = surveyResult;

        return true;
    }

    public bool TryGetMovementDirection(Vector3d origin, out Vector3d direction)
    {
        direction = Vector3d.Zero;
        if (!FlowMap.HasPath)
            return false;

        direction = FlowFieldSurveyor.SampleFlowVector(origin, FlowMap.Fields);
        if (direction == Vector3d.Zero)
            return false;

        direction = direction.Normal;
        return true;
    }

    public bool FlowFieldContainsPosition(Vector3d origin)
    {
        if (!FlowMap.HasPath
            || !GlobalGridManager.TryGetVoxel(origin, out Voxel currentVoxel)
            || !FlowMap.Fields.ContainsKey(currentVoxel.SpawnToken))
        {
            return false;
        }

        return true;
    }

    public bool TryGetFallbackDirection(Vector3d origin, out Vector3d fallbackDirection)
    {
        fallbackDirection = Vector3d.Zero;
        if (!FlowMap.HasPath
            || !GlobalGridManager.TryGetVoxel(origin, out Voxel currentVoxel)
            || !FlowMap.Fields.ContainsKey(currentVoxel.SpawnToken))
        {
            return false;
        }

        bool voxelFound = FlowFieldSurveyor.TryGetNearestFlowAnchor(origin,
            FlowMap.Fields,
            DefaultFieldSearchRange,
            out Voxel destination);
        if (!voxelFound)
            return false;

        fallbackDirection = (destination.WorldPosition - origin).Normalize();
        return true;
    }
}
