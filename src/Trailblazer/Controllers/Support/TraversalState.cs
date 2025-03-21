using FixedMathSharp;

using Trailblazer.Controllers;

/// <summary>
/// Represents the current traversal state and provides synchronization with TraversalCondition.
/// </summary>
public class TraversalState
{
    /// <summary>
    /// The current traversal medium.
    /// </summary>
    public TraversalMedium Medium { get; private set; }

    /// <summary>
    /// The height of the surface the entity is interacting with.
    /// </summary>
    public Fixed64 SurfaceLevel { get; private set; }

    /// <summary>
    /// The ground state, if applicable.
    /// </summary>
    public SurfaceCondition? SurfaceState { get; private set; }

    /// <summary>
    /// The ceiling height above the entity.
    /// </summary>
    public Fixed64 CeilingLevel { get; private set; } = Fixed64.MAX_VALUE;

    /// <summary>
    /// The normal of the ground surface.
    /// </summary>
    public Vector3d SurfaceNormal { get; private set; }

    public Fixed64 SlopeAngle { get; private set; }

    /// <summary>
    /// The previous traversal state (for comparison and transition detection).
    /// </summary>
    public TraversalCondition? PreviousState { get; private set; }

    public TraversalState(TraversalCondition condition)
    {
        Update(condition, null);
    }

    /// <summary>
    /// Updates the traversal state and retains the previous state for transition tracking.
    /// </summary>
    public void Update(TraversalCondition condition, TraversalCondition? previous)
    {
        PreviousState = previous;
        Medium = condition.Medium;
        SurfaceLevel = condition.SurfaceLevel;
        SurfaceState = condition.SurfaceCondition;

        if(Medium == TraversalMedium.Ground)
        {
            SurfaceNormal = SurfaceState?.SurfaceNormal ?? Vector3d.Zero;
            SlopeAngle = Vector3d.Angle(Vector3d.Up, SurfaceNormal);
            bool isDownhill = Vector3d.Dot(Vector3d.Forward, SurfaceNormal) < Fixed64.Zero;
            if (isDownhill) SlopeAngle *= -1;
        }
        else
        {
            SurfaceNormal = Vector3d.Zero;
            SlopeAngle = Fixed64.Zero;
        }

        CeilingLevel = condition.CeilingLevel;
    }

    /// <summary>
    /// Returns a new TraversalCondition instance reflecting the current state.
    /// </summary>
    public TraversalCondition ToTraversalCondition()
    {
        return new TraversalCondition(Medium, SurfaceLevel, SurfaceState, CeilingLevel);
    }
}
