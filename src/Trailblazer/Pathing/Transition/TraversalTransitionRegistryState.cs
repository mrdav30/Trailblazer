using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores traversal transition registrations and lookup indexes for one pathing context.
/// </summary>
internal sealed class TraversalTransitionRegistryState
{
    internal SwiftDictionary<string, RegisteredTraversalTransition> Transitions { get; } =
        new(8, StringComparer.Ordinal);

    internal SwiftHashSet<string> ActiveTransitionIds { get; } = new();

    internal SwiftHashSet<string> SuppressedManagedTransitionIds { get; } = new();

    internal SwiftDictionary<WorldVoxelIndex, SwiftHashSet<string>> ManagedManualTransitionIdsByVoxel { get; } = new();

    internal SwiftDictionary<WorldVoxelIndex, SwiftHashSet<string>> OutgoingTransitionIdsByVoxel { get; } = new();

    internal SwiftDictionary<WorldVoxelIndex, SwiftHashSet<string>> IncomingTransitionIdsByVoxel { get; } = new();

    internal SwiftDictionary<int, SwiftHashSet<string>> TransitionIdsBySourceGrid { get; } = new();

    internal SwiftDictionary<int, SwiftHashSet<string>> TransitionIdsByDestinationGrid { get; } = new();

    internal ReaderWriterLockSlim TransitionLock { get; } = new();

    internal int RegistryVersion;

    internal int RegistrationOrder;

    internal TraversalTransition[] AllTransitionsSnapshot = Array.Empty<TraversalTransition>();

    internal void IncrementRegistryVersion()
    {
        Interlocked.Increment(ref RegistryVersion);
    }
}
