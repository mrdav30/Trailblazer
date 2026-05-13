namespace Trailblazer.Pathing;

/// <summary>
/// Describes who owns a registered transition's lifecycle.
/// </summary>
internal enum TraversalTransitionOwnershipKind
{
    ManagedManual = 0,
    ManagedGenerated = 1
}
