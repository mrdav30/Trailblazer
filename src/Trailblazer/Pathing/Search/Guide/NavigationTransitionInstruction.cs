//=======================================================================
// NavigationTransitionInstruction.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Describes one exact semantic action selected by a navigation guide.</summary>
public readonly struct NavigationTransitionInstruction
{
    internal NavigationTransitionInstruction(
        NavigationTransitionIdentityKind identityKind,
        string ownerMapId,
        string id,
        TraversalTransitionType type,
        NavigationCellAddress sourceAddress,
        NavigationCellAddress destinationAddress,
        TraversalMedium sourceMedium,
        TraversalMedium destinationMedium,
        Vector3d sourcePosition,
        Vector3d destinationPosition,
        TraversalTransitionLocomotionHints locomotionHints,
        object? completionOwner = null,
        ulong acquisitionGeneration = 0,
        long stepOrdinal = -1)
    {
        IdentityKind = identityKind;
        OwnerMapId = ownerMapId;
        Id = id;
        Type = type;
        SourceAddress = sourceAddress;
        DestinationAddress = destinationAddress;
        SourceMedium = sourceMedium;
        DestinationMedium = destinationMedium;
        SourcePosition = sourcePosition;
        DestinationPosition = destinationPosition;
        LocomotionHints = locomotionHints;
        CompletionOwner = completionOwner;
        AcquisitionGeneration = acquisitionGeneration;
        StepOrdinal = stepOrdinal;
    }

    /// <summary>Gets whether the stable ID belongs to a definition or a rule.</summary>
    public NavigationTransitionIdentityKind IdentityKind { get; }

    /// <summary>Gets the explicit definition owner map ID, or an empty string for a rule.</summary>
    public string OwnerMapId { get; }

    /// <summary>Gets the stable transition definition or rule ID.</summary>
    public string Id { get; }

    /// <summary>Gets the authored semantic action type.</summary>
    public TraversalTransitionType Type { get; }

    /// <summary>Gets the exact source-cell address.</summary>
    public NavigationCellAddress SourceAddress { get; }

    /// <summary>Gets the exact destination-cell address.</summary>
    public NavigationCellAddress DestinationAddress { get; }

    /// <summary>Gets the exact source medium.</summary>
    public TraversalMedium SourceMedium { get; }

    /// <summary>Gets the exact destination medium.</summary>
    public TraversalMedium DestinationMedium { get; }

    /// <summary>Gets the exact source action position.</summary>
    public Vector3d SourcePosition { get; }

    /// <summary>Gets the exact destination action position.</summary>
    public Vector3d DestinationPosition { get; }

    /// <summary>Gets the authored built-in locomotion hints.</summary>
    public TraversalTransitionLocomotionHints LocomotionHints { get; }

    private ulong AcquisitionGeneration { get; }

    private long StepOrdinal { get; }

    private object? CompletionOwner { get; }

    internal NavigationTransitionInstruction WithCompletionStamp(
        object completionOwner,
        ulong acquisitionGeneration,
        long stepOrdinal) => new(
        IdentityKind,
        OwnerMapId,
        Id,
        Type,
        SourceAddress,
        DestinationAddress,
        SourceMedium,
        DestinationMedium,
        SourcePosition,
        DestinationPosition,
        LocomotionHints,
        completionOwner,
        acquisitionGeneration,
        stepOrdinal);

    internal bool MatchesCompletion(
        object completionOwner,
        ulong acquisitionGeneration,
        long stepOrdinal) =>
        ReferenceEquals(CompletionOwner, completionOwner)
        && AcquisitionGeneration == acquisitionGeneration
        && StepOrdinal == stepOrdinal;
}
