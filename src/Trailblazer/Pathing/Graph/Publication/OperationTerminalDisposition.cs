//=======================================================================
// OperationTerminalDisposition.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Classifies how a completed operation leaves the runtime queue.</summary>
internal enum OperationTerminalDisposition : byte
{
    None,
    Release,
    ReleaseAndBeginRollback,
    ReleaseAndTryReopen
}
