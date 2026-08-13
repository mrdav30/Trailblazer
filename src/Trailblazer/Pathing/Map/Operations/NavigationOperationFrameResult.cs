//=======================================================================
// NavigationOperationFrameResult.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

internal enum NavigationCandidatePublication
{
    Published = 0,
    PermanentCapacity = 1,
    Deferred = 2
}

internal enum NavigationOperationFrameResult
{
    None = 0,
    Published = 1,
    Rejected = 2,
    Deferred = 3
}
