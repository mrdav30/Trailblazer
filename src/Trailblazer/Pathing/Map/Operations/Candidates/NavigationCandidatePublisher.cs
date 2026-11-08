//=======================================================================
// NavigationCandidatePublisher.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Attempts to publish an admitted navigation operation candidate.</summary>
internal delegate NavigationCandidatePublication NavigationCandidatePublisher(
    NavigationOperationCandidate candidate,
    long frame,
    NavigationOperationFrameChange[] changes,
    int changeCount);
