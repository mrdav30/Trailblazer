//=======================================================================
// AutomaticSeamAdvanceDisposition.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports whether automatic seam work can continue in the current frame.</summary>
internal enum AutomaticSeamAdvanceDisposition : byte
{
    Continue,
    Retain,
    Restart,
    Complete
}
