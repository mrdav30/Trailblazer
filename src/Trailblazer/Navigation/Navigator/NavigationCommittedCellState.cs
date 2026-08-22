//=======================================================================
// NavigationCommittedCellState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Describes the effective navigation cell occupied after one controller motion commit.
/// </summary>
public readonly struct NavigationCommittedCellState
{
    internal NavigationCommittedCellState(
        NavigationCellAddress address,
        NavigationAreaId area,
        TraversalMedium medium,
        long graphVersion,
        NavigationAreaPolicyKey? areaPolicy,
        int simulationFrame)
    {
        Address = address;
        Area = area;
        Medium = medium;
        GraphVersion = graphVersion;
        AreaPolicy = areaPolicy;
        SimulationFrame = simulationFrame;
    }

    /// <summary>Gets the durable map and topology-local cell address.</summary>
    public NavigationCellAddress Address { get; }

    /// <summary>Gets the host-authored area of the effective cell.</summary>
    public NavigationAreaId Area { get; }

    /// <summary>Gets the controller's committed traversal medium.</summary>
    public TraversalMedium Medium { get; }

    /// <summary>Gets the immutable graph generation used to resolve the cell.</summary>
    public long GraphVersion { get; }

    /// <summary>Gets the active guided-query area policy, when guidance owns one.</summary>
    public NavigationAreaPolicyKey? AreaPolicy { get; }

    /// <summary>Gets the deterministic simulation frame at which motion was committed.</summary>
    public int SimulationFrame { get; }
}
