# Benchmark Signal Hardening Backlog

## Purpose

This document captures benchmark-derived hardening signals that fall outside the
active feature plan. It is intentionally undated and long-lived: individual
entries carry their own discovery dates, evidence, status, and next isolation
step.

Use this backlog for measured performance, allocation, scaling, and benchmark
evidence concerns. Bugs or correctness risks that are not primarily benchmark
signals belong in [`issue-tracker.md`](issue-tracker.md). Broad feature or
architecture work should be promoted into its own dated plan and referenced from
this backlog.

## Intake Rules

- Signal IDs use `TRB-Benchmark-NNN`. The next available ID is
  `TRB-Benchmark-001`.
- Assign an ID at intake and never reuse it, including after a signal closes or
  moves into a dated plan. Check this file's Git history before advancing or
  repairing the counter.
- Add a signal only when it comes from a benchmark, allocation guardrail,
  profiler trace, or repeated validation run.
- Record the command, date, affected row or test, measured value, why it
  matters, and the smallest useful next isolation step.
- Keep benchmark-only instrumentation in tests or benchmark support unless the
  runtime needs a durable diagnostic API.
- Prefer a focused fix when the signal has a narrow cause.
- Promote to a dated feature-work plan when the signal spans multiple
  subsystems, requires API design, or needs staged implementation.
- Close entries only after a runtime/test/docs change lands or after a written
  no-change decision explains why the signal is expected.

## Baseline Commands

Build the benchmark project before capturing evidence:

```powershell
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0
```

List the continuous-collision evidence rows:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll continuous-collision-evidence --list flat
```

Run the current continuous-collision evidence smoke:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll continuous-collision-evidence --filter "*Evidence*" -j Short -i
```

After runtime changes, validate the package paths:

```powershell
dotnet test Trailblazer.slnx --configuration Release
dotnet test Trailblazer.slnx --configuration ReleaseLean
```

## Active Signals

No active benchmark signals remain. Add new measured concerns here before they
are promoted into implementation work.

## Experimental Signals

| Signal | Status | Revisit When |
| ------ | ------ | ------------ |
| n/a    | n/a    | n/a          |

## Closed Signals

| Signal | Status | Closed | Resolution |
| ------ | ------ | ------ | ---------- |
| n/a    | n/a    | n/a    | n/a        |

## Watch Items

No watch items remain. Add new measured concerns here before they are promoted
into implementation work.

## Promotion Criteria

Promote a signal from this backlog into a dedicated dated plan when it has:

- reproducible evidence and a suspected runtime phase.
- enough subsystem breadth that a single focused patch would be misleading.
- API, architecture, or multi-workstream design decisions.
- correctness or ordering invariants that need staged implementation.
- benchmark and allocation evidence that should move with the new plan.

## Current Recommendation

All release-relevant benchmark signals are closed. Dense concave mesh/mesh
throughput remains experimental capacity guidance; prefer primitive, convex,
compound, or partitioned static-concave authoring. Keep this document as the
intake bucket for future measured signals and promote broader work into a dated
feature plan when the scope outgrows a focused patch.
