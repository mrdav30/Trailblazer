namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Provides host-owned climb affordance snapshots to the navigation runtime.
/// </summary>
public interface IClimbAffordanceResolver
{
    /// <summary>
    /// Attempts to resolve the current climb affordance for the given traversal request and state.
    /// </summary>
    bool TryResolveClimbAffordance(TrekRequest request, TransitState currentState, out ClimbAffordanceSnapshot snapshot);
}
