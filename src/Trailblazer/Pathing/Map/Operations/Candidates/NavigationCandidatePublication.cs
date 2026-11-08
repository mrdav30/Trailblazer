//=======================================================================
// NavigationCandidatePublication.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports the outcome of attempting to publish a navigation candidate.</summary>
internal enum NavigationCandidatePublication
{
    Published = 0,
    PermanentCapacity = 1,
    Deferred = 2
}
