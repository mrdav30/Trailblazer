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
  `TRB-Benchmark-002`.
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

Build the benchmark project against the coordinated local stack before capturing
evidence while upstream feature changes remain unreleased:

```powershell
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -p:UseLocalLsfStack=true
```

List the available navigation benchmark selections:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll list
```

Run a public guide-service smoke check (not performance-decision evidence):

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-guide-service --job short --inProcess
```

After runtime changes, validate the package paths:

```powershell
dotnet test Trailblazer.slnx --configuration Release
dotnet test Trailblazer.slnx --configuration ReleaseLean
```

## Active Signals

No active benchmark signals remain.

## Closed Signals

### TRB-Benchmark-001 - Guide acquisition exceeds the provisional navigation allowance

- **Discovered:** 2026-09-08
- **Status:** Closed locally on 2026-09-09; focused optimizations and the approved
  [deterministic scheduling plan](done/navigationSchedulingPlan.md) are implemented,
  verified and independently reviewed. CI/released-package validation remains
  part of the coordinated release gates, not a claim of this local closeout.
- **Evidence:** The Phase 3 out-of-process `SlowAcquisition` protocol measures
  median public Flow acquisition of 5.51 seconds at 10K nodes and 55.42 seconds
  at 100K nodes; public A* medians are 2.14 and 21.92 seconds. Four distinct cold
  requests produce median containing-frame costs of 21.73 and 221.11 seconds.
  Every large cold sample exceeds the full 31.25 ms frame. The canonical 100K
  four-warm-request frame also exceeds the 2 ms allowance in all 300 samples
  (minimum 2.535 ms; sample P99 2.810 ms). Distributions, counters and artifacts are in
  [the acquisition execution record](done/navigationHardeningPlan.md#phase-3---measurement-protocol-and-baseline).
- **Budget:** Provisional 32 Hz headless host, 2 ms navigation allowance, 100
  existing Navigators and bursts of one or four requests. Finite query work
  limits do not bound elapsed time to one frame.
- **Reproduction:** Run the cold guide/frame and canonical-control commands in the
  [benchmark guide](../../tests/Trailblazer.Benchmarks/README.md#cold-public-acquisition-and-containing-frames)
  against the recorded local source stack without competing builds or load.
- **Investigation:** The [request-root profile and source audit](done/navigationSchedulingPlan.md)
  attributes about 91-92% of sampled cold Flow/A* time to the simple surface-route
  path, including unused Release diagnostic geometry. Warm 100K acquisition
  spends about 95-97% in dependency validation; six full passes visit 9,378 page
  entries per successful acquisition in this workload. These overlapping sampled
  shares are attribution, not predicted speedups. `TRB-Issue-114` is closed as
  an inconclusive investigation in the [issue tracker](issue-tracker.md); its
  unattributed covered-test observation does not confound these separate
  acquisition traces, and no production allocator fix is claimed.
- **Focused fix result:** After resolving `TRB-Issue-115`, matched cold public
  Flow medians are 1.864 seconds at 10K and 18.924 seconds at 100K; A* medians
  are 0.717 and 7.343 seconds. The same nine-sample protocol shows 65.85-66.50%
  median reductions against the retained historical baseline, with unchanged
  allocations and reported reference counters. Every selected sample still
  exceeds the full frame allowance. Warm/frame performance was not remeasured;
  the [execution record](done/navigationSchedulingPlan.md#cpu-cost-improvements)
  retains the distributions, provenance limits and remaining decision.
- **Further focused result:** Guarded rectangular portal reuse and method-local
  dependency-proof reuse reduce cold Flow medians to 293.612 ms / 3,225.761 ms
  and A* medians to 87.841 ms / 904.471 ms at 10K / 100K. All 36 cold samples
  still exceed 31.25 ms. The directly measured 100K four-warm-request frame now
  has observed P99 1.870 ms and maximum 1.915 ms across 300 samples, with
  unchanged allocations and reference counters. See the
  [full follow-up evidence](done/navigationSchedulingPlan.md#cpu-cost-improvements).
- **Final result:** The scheduler-enabled build's four-warm-request P99 is
  1.940 ms across 300 observations, with one 2.129 ms excursion. All 81 scheduled
  actual operations have observed containing-frame P99 below 2 ms (highest
  1.933 ms). The cold cases retain 653,175 frames, including 34 above 2 ms and
  none above 31.25 ms; maximum is 3.924 ms. A* publication reaches 2.669 ms, so
  completion is not universally below the allowance. Both configurations retain
  exact 100% line/branch/method coverage.
- **Disposition:** Warm reuse meets the selected observed-P99 allowance.
  Synchronous cold acquisition remains rejected for the measured fixed frame;
  use loading/off-frame preparation or explicit scheduling. Scheduling preserves
  deterministic ownership and bounds continuation work but has substantial FIFO
  latency: 100K Flow takes about 6 simulated minutes for one request and 24.25
  minutes for the fourth at these conservative defaults. The
  [complete results and limits](done/navigationSchedulingPlan.md#final-disposition)
  retain every phase, outlier, completion frame and binary identity. No universal
  timing guarantee, automatic Navigator queuing or further speculative search
  optimization is implied by closure.

## Experimental Signals

| Signal | Status | Revisit When |
| ------ | ------ | ------------ |
| n/a    | n/a    | n/a          |

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

Use the current [benchmark guide](../../tests/Trailblazer.Benchmarks/README.md)
for navigation workloads and canonical run settings. Keep this document as the
intake bucket for measured signals and promote broader work into a dated feature
plan when the scope outgrows a focused patch. Physics collision-shape throughput
belongs to Gravitas, not this navigation backlog.
