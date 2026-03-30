namespace Trailblazer.Pathing;

/// <summary>
/// Describes who owns a registered transition's lifecycle.
/// </summary>
internal enum TraversalTransitionOwnershipKind
{
    RawManual = 0,
    ManagedManual = 1,
    ManagedGenerated = 2
}
