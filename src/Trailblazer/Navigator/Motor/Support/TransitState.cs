using FixedMathSharp;
using Trailblazer.Navigation.Motor;

/// <summary>
/// Represents the current traversal state and provides synchronization with TraversalCondition.
/// </summary>
public class TransitState
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
    /// The ceiling height above the entity.
    /// </summary>
    public Fixed64 CeilingLevel { get; private set; } = Fixed64.MAX_VALUE;

    /// <summary>
    /// The ground state, if applicable.
    /// </summary>
    public GroundCondition? GroundState { get; private set; }

    /// <summary>
    /// The normal of the ground surface.
    /// </summary>
    public Vector3d SurfaceNormal { get; private set; }

    public Fixed64 SlopeAngle { get; private set; }

    /// <summary>
    /// The previous traversal state (for comparison and transition detection).
    /// </summary>
    public TraversalCondition? PreviousState { get; private set; }

    public TransitState(TraversalCondition condition)
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
        GroundState = condition.GroundState;

        if(Medium == TraversalMedium.Ground)
        {
            SurfaceNormal = GroundState?.GroundNormal ?? Vector3d.Zero;
            SlopeAngle = Vector3d.Angle(Vector3d.Up, SurfaceNormal);
            bool isDownhill = Vector3d.Dot(Vector3d.Forward, SurfaceNormal) < Fixed64.Zero;
            if (isDownhill) SlopeAngle *= -1;
        }
        else
        {
            SurfaceNormal = Vector3d.Up;
            SlopeAngle = Fixed64.Zero;
        }

        CeilingLevel = condition.CeilingLevel;
    }

    /// <summary>
    /// Returns a new TraversalCondition instance reflecting the current state.
    /// </summary>
    public TraversalCondition ToTraversalCondition()
    {
        return new TraversalCondition(Medium, SurfaceLevel, GroundState, CeilingLevel);
    }
}
