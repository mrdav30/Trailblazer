//=======================================================================
// ExternalGridEventObservation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

internal readonly struct ExternalGridEventObservation
{
    public ExternalGridEventObservation(
        ExternalGridEventSignature signature,
        int identicalEventStreak)
    {
        Signature = signature;
        IdenticalEventStreak = identicalEventStreak;
    }

    public ExternalGridEventSignature Signature { get; }

    public int IdenticalEventStreak { get; }
}
