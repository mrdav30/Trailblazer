//=======================================================================
// PolicyFrameDisposition.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports the outcome of advancing pending area policies.</summary>
internal enum PolicyFrameDisposition : byte
{
    None,
    Complete,
    RejectCapacity
}
