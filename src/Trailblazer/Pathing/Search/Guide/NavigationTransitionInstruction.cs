//=======================================================================
// NavigationTransitionInstruction.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Describes one exact semantic action selected by a navigation guide.</summary>
internal readonly struct NavigationTransitionInstruction
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

    internal NavigationTransitionIdentityKind IdentityKind { get; }

    internal string OwnerMapId { get; }

    internal string Id { get; }

    internal TraversalTransitionType Type { get; }

    internal NavigationCellAddress SourceAddress { get; }

    internal NavigationCellAddress DestinationAddress { get; }

    internal TraversalMedium SourceMedium { get; }

    internal TraversalMedium DestinationMedium { get; }

    internal Vector3d SourcePosition { get; }

    internal Vector3d DestinationPosition { get; }

    internal TraversalTransitionLocomotionHints LocomotionHints { get; }

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
