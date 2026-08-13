//=======================================================================
// NavigationAreaPolicyCommitOperation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Describes one deterministic immutable navigation-area policy publication.</summary>
public readonly struct NavigationAreaPolicyCommitOperation
{
    /// <summary>Creates a policy publication and its pending receipt.</summary>
    public NavigationAreaPolicyCommitOperation(
        NavigationAreaPolicy policy,
        long publicationSequence,
        int effectiveFrame)
    {
        SwiftThrowHelper.ThrowIfNull(policy, nameof(policy));
        NavigationOperationValidation.ValidateSchedule(publicationSequence, effectiveFrame);
        Policy = policy;
        PublicationSequence = publicationSequence;
        EffectiveFrame = effectiveFrame;
        Receipt = new NavigationOperationReceipt(publicationSequence, effectiveFrame);
    }

    /// <summary>Gets the immutable policy revision.</summary>
    public NavigationAreaPolicy Policy { get; }

    /// <summary>Gets the unique sequence in the context's policy-publication stream.</summary>
    public long PublicationSequence { get; }

    /// <summary>Gets the earliest eligible fixed-step publication frame.</summary>
    public int EffectiveFrame { get; }

    /// <summary>Gets the operation's deterministic receipt.</summary>
    public NavigationOperationReceipt Receipt { get; }
}
