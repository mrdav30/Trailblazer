//=======================================================================
// NavigationOperationStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the deterministic lifecycle state of a submitted navigation operation.</summary>
public enum NavigationOperationStatus : byte
{
    /// <summary>The operation is admitted but has not published or terminated.</summary>
    Pending = 0,

    /// <summary>The operation was included in an immutable candidate publication.</summary>
    Applied = 1,

    /// <summary>The operation failed validation or admission without changing the candidate.</summary>
    Rejected = 2,

    /// <summary>A later operation made the complete operation observably redundant.</summary>
    Superseded = 3
}
