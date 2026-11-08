//=======================================================================
// SeamAdvanceStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports the outcome of advancing an automatic seam refresh.</summary>
internal enum SeamAdvanceStatus : byte
{
    Blocked,
    Progressed,
    Complete
}
