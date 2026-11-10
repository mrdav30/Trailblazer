//=======================================================================
// NavigationTransitionInstruction2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Describes one lease-issued planar semantic action.</summary>
public readonly struct NavigationTransitionInstruction2D
{
    internal NavigationTransitionInstruction2D(in NavigationTransitionInstruction instruction) => Instruction = instruction;

    internal NavigationTransitionInstruction Instruction { get; }

    /// <summary>Gets the stable transition ID within its owner map.</summary>
    public string Id => Instruction.Id;

    /// <summary>Gets the map that owns the authored transition.</summary>
    public string OwnerMapId => Instruction.OwnerMapId;

    /// <summary>Gets the authored semantic action type.</summary>
    public TraversalTransitionType Type => Instruction.Type;

    /// <summary>Gets the exact source cell address.</summary>
    public NavigationCellAddress SourceAddress => Instruction.SourceAddress;

    /// <summary>Gets the exact destination cell address.</summary>
    public NavigationCellAddress DestinationAddress => Instruction.DestinationAddress;

    /// <summary>Gets the traversal medium before the action.</summary>
    public TraversalMedium SourceMedium => Instruction.SourceMedium;

    /// <summary>Gets the traversal medium after the action.</summary>
    public TraversalMedium DestinationMedium => Instruction.DestinationMedium;

    /// <summary>Gets the canonical source center in world X/Z.</summary>
    public Vector2d SourcePosition => Instruction.SourcePosition.ToVector2d();

    /// <summary>Gets the canonical destination center in world X/Z.</summary>
    public Vector2d DestinationPosition => Instruction.DestinationPosition.ToVector2d();

    internal bool Matches(in NavigationTransitionInstruction2D other) =>
        Instruction.HasSameCompletion(other.Instruction);
}
