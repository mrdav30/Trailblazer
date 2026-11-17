//=======================================================================
// SupportQueryPolicy.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Chronicler.Diagnostics;
using Gravitas.Queries;

namespace Trailblazer.Navigation;

internal struct SupportQueryPolicy
{
    internal PhysicsQueryBudget Budget { get; private set; }
    internal PhysicsQueryScratch? Scratch { get; private set; }
    internal PhysicsQueryExecutionStatus? ExecutionStatus { get; private set; }
    internal PhysicsQueryWork? Work { get; private set; }

    internal void Configure(PhysicsQueryBudget budget, PhysicsQueryScratch scratch)
    {
        ChronicleGuard.ThrowIfNull(scratch, nameof(scratch));
        Budget = budget;
        Scratch = scratch;
        ResetDiagnostics();
    }

    internal void ResetDiagnostics()
    {
        ExecutionStatus = null;
        Work = null;
    }

    internal void Record(PhysicsQueryExecutionStatus status, in PhysicsQueryWork work)
    {
        ExecutionStatus = status;
        Work = work;
    }
}
