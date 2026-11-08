//=======================================================================
// AdvanceStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports the outcome of advancing automatic seam lifecycle work.</summary>
internal enum AdvanceStatus : byte
{
    Blocked,
    Progressed,
    Complete,
    RestartRequired,
    CapacityExceeded
}
