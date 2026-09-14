# Deterministic Line-of-Sight Phase Spreading Implementation Plan

> **For agentic workers:** Execute only when implementation is requested. Use
> the phase checkboxes and independent contract, correctness and Ponytail
> reviews. Keep decisions and evidence in this document rather than creating
> parallel plans. This draft does not start runtime implementation.

**Created:** 2026-09-14

**Status:** In progress; Phases 0 and 1 complete. Narrow steering correction
precedes Phase 2 performance capture.

**Execution branch:** `codex/los-phase-spreading`, created from `develop` at
`5367f5a` after the user requested branch-based implementation and final review.

**Source baseline:** Trailblazer `bc5349cc44db0ac4cdba183bacebaef0fe91530f`.
Record the exact sibling revisions and executed binaries again before capture.

**Goal:** Let hosts spread recurring Navigator line-of-sight (LOS) refreshes
across deterministic fixed frames, reducing synchronized work spikes without
silently reducing refresh frequency or delaying required validation.

**Architecture:** One explicit per-steering phase policy shared by A* and Flow.
The host assigns phases; steering decides when its periodic refresh is due.
Do not introduce a world queue, worker scheduler or automatic agent registry.

**Tech Stack:** C# 11; `netstandard2.1` and `net8.0`; the existing context clock,
NavSteering, Navigator, Chronicler, xUnit v3 and BenchmarkDotNet infrastructure.

**Spec:** [Scope and constraints](#scope-and-constraints) and
[Proposed timing contract](#proposed-timing-contract) define the intended
behavior. Phase 0 freezes exact signatures, state transitions and wire fields
before production changes. Any material departure returns here for review.

## Why this work comes next

The bounded micro-optimization sweep for
[`TRB-Benchmark-005`](benchmark-signal-hardening-backlog.md#trb-benchmark-005---guided-frames-spend-most-time-in-long-blocked-segment-tracing)
has ended. Keep its implemented geometry/dependency improvements and do not
revive withdrawn experiments without new evidence.

The final unchanged Flow/500 control records about **4.78 ms** on ordinary
frames and **57.64 ms** on synchronized LOS frames. All 36 measured rechecks
exceed the 31.25 ms full-frame budget; all 540 ordinary frames exceed the 2 ms
navigation allowance. These are historical local headless containing-frame
observations, not pure Flow sampling costs or portable throughput guarantees.

Today `PathRecheckCooldownFrames` defaults to 16, and `_pathCheckCooldown`
decrements in `FinalizeHeadingFrame`. It counts completed heading updates,
not elapsed context frames. At one eligible update per fixed frame and 32 Hz,
that is normally one refresh every half-second, **not every frame**. Agents
that start and progress together consequently refresh together.

Assigning 500 continuously active agents evenly across 16 slots gives 31-32
periodic refreshes per slot. That is a work-count example, not a latency
prediction: rays differ in cost, all ordinary controller work remains, and
simultaneous admission or invalidation can still burst.

## Scope and constraints

- Opt-in phase spreading for periodic LOS refreshes in shared `NavSteering`,
  including A*, Flow, direct travel and movement-group followers where that
  steering path applies. Do not create algorithm-specific scheduling APIs.
- Preserve the current phase-disabled countdown behavior, default interval and
  existing immediate-check behavior, including the zero-interval use exercised
  by tests. Characterize non-positive values before changing validation.
- Preserve existing guide/dependency checks, query budgets, fail-closed statuses,
  action ownership, movement simulation and motion commits. A phase is not
  permission to use an invalid guide or skip collision/occupancy work.
- Retain fixed-point math and deterministic host ordering. No random phases,
  runtime hashes, wall-clock timers, background workers, allocation per tick or
  automatic construction-order assignment. Hosts may use stable ordinals.
- Keep the original synchronized benchmark as the control. Add distinct phased
  cases without replacing its timings or weakening its semantic checks.
- Use existing tests, benchmark helpers and analysis scripts. No new regression
  project, CI framework, telemetry platform or general-purpose scheduler.
- Keep public configuration small and unambiguous. No compatibility forwarding
  overloads or parallel ways to configure the same policy. Put any new
  non-private type in its own file; use SwiftThrowHelper for guards.
- Preserve exact reachable line, branch and fully covered method coverage in
  Release and ReleaseLean. Add observable behavior tests, not API snapshots or
  existence-only tests. No new coverage exclusions to make the numbers pass.
- Keep this slice in Trailblazer unless a reproduced upstream defect requires
  an owning-library fix. Capture defects in [issue-tracker.md](issue-tracker.md).
  Releases remain deferred under the [overview](feature-work-overview.md).
- Work on the requested `codex/los-phase-spreading` feature branch from
  `develop`, preserving unrelated worktree changes. Pushes, merges and releases
  still require the corresponding request.

### Not included

Lower refresh frequency, adaptive distance/visibility heuristics, sharing LOS
results between agents, resumable individual rays, staggered initial path
acquisition, or a new geometry/cache optimization pass. The existing scheduled
guide-acquisition service remains separate and does not own these refreshes.
Engine-specific visual playtesting and competitor benchmarking are not gates.

## Proposed timing contract

These are the recommended semantics to settle in Phase 0, not an already
available public API. Preserve one unphased default and make the changed timing
explicit when a host opts in.

| Situation | Required behavior |
| --- | --- |
| Phase disabled | Existing completed-heading countdown behavior remains unchanged. |
| Phase enabled | Positive interval `I`, explicit phase `P` in `[0, I)`, aligned to the owning context's fixed-frame clock. Phase zero is a real slot, not the disabled sentinel. |
| Normal continuous movement | One periodic refresh each `I` context frames, on the assigned slot. Do not suppress intermediate steering, guide validation, sampling or motion commits. |
| Initial admission / required retry | Use the existing admission or retry boundary without waiting for the phase. After that check, schedule the next periodic slot strictly in the future, within `1..I` frames. Do not add `I + P` to the interval. |
| Multiple heading calls in one context frame | Do not repeat a phased periodic refresh solely because the host called again. This does not suppress independently required admission or retry work. |
| Pause, skipped updates, or held action | No background refresh. If a periodic slot was missed, refresh once on the next eligible heading update, then realign. Do not replay every missed slot or wait another full interval after resuming. |
| Stop / arrival / new destination | Discard old session timing debt. Starting a new guided request still follows immediate admission; retain the host's configured phase. |
| Runtime interval / phase changes | Validate the complete configuration before mutation. Apply at a deterministic host boundary. Do not defer already-due work; otherwise choose a next slot within the new interval. Document disabling as a return to the ordinary countdown. |
| Profile / medium changes and stale guidance | Preserve existing re-admission, zero-heading and action-completion rules. Keep the host phase; never phase-delay required recovery. |
| Context reset / rebind | Do not carry deadlines into a different clock lifetime. Reset session timing and establish its next slot only for a valid new session. No registration or disposal ownership is added. |
| Save / load | Persist explicit configuration and only the timing state required by the reviewed restore contract. Never restore a lease or dependency proof as valid merely because a phase has not arrived. |

Here, **immediate** means *not delayed by phase spreading*. It does not change
an existing next-heading retry into a same-call reacquisition, or force work
while the Navigator is holding an uncompleted transition.

For example, with `I = 16`, `P = 3` and admission at context frame 0, normal
periodic checks occur at frames 3, 19 and 35. If updates are paused across frame
19 and resume at 22, perform one overdue refresh at 22, then return to slot 35.
For `P = 0`, admission at frame 0 is followed by slot 16, not another periodic
check in frame 0. These are proposed timing examples, not callable C# snippets.

Phase alignment changes when agents discover that direct travel has become
available or blocked. Enabled and disabled trajectories need not have identical
hashes. Identical inputs, phases and clock histories **within the same mode**
must replay exactly. A population resuming together can still produce an
overdue-refresh burst; do not disguise that as a scheduling guarantee.

### Boundaries requiring explicit proof

- `FrameCount` is currently a signed `int`, and the context clock is not part
  of Navigator serialization. Define overflow/reset arithmetic and same-clock
  versus new-clock load behavior; do not assume a restorable global epoch or
  add a clock-restoration API as a side effect of this feature.
- Current load restores direct-mode flags differently from guide-backed
  sessions. Inspect both paths before claiming that every loaded session already
  performs fresh admission. Specify how enabled-mode restore validates current
  travel before using it, while preserving phase-disabled behavior. Treat any
  pre-existing correctness defect as a separately recorded issue.
- Navigator validates nested records using a temporary NavSteering shell.
  Validation must not assign a phase, register an agent, mutate the world, or
  alter live scheduling/guide state if a later field rejects.
- A phase controls only the periodic opportunity to refresh LOS. Distinguish
  that latency from existing freshness checks; do not advertise this feature
  as new per-frame obstacle detection or physical collision protection.

## File responsibilities

Paths are relative to the repository. New paths below are proposed additions;
Phase 0 records any minimal adjustment alongside the approved API.

| Area | Files / responsibility |
| --- | --- |
| Configuration and due-state | `src/Trailblazer/Navigation/Steering/NavSteering.cs`; keep the small policy here, with a separate file only if a new value type or cohesive partial is justified. |
| Shared periodic gate | `src/Trailblazer/Navigation/Steering/NavSteering.Simulation.cs`; distinguish periodic timing from existing admission/retry paths. |
| Profile and query lifecycle | `src/Trailblazer/Navigation/Steering/NavSteering.ProfileReplacement.cs`, `NavSteering.Requests.cs`, and the existing Navigator transition/reset boundaries as needed. |
| Records and staged load | `src/Trailblazer/Navigation/Steering/NavSteering.Serialization.cs`, `src/Trailblazer/Navigation/Navigator/Navigator.Serialization.cs`; explicit field validation and schema decision. |
| Behavioral acceptance | New `tests/Trailblazer.Tests/Navigation/Steering/NavSteeringLineOfSightPhase.Tests.cs`; extend existing Navigator, serialization, profile-replacement and transition tests rather than duplicating their fixtures. |
| Full-frame evidence | `tests/Trailblazer.Benchmarks/Pathing/NavigationGuidedFrameBenchmarks.cs`, `Support/BenchmarkNavigator.cs`, `AnalyzeGuidedFrames.ps1`, `TestGuidedFrameAnalysis.ps1`; retain original case identity and add distinct phased identities. |
| User documentation | `docs/wiki/NavSteering.md`, relevant `Navigator.md` / `Serialization.md` guidance, public XML, benchmark README, and `docs/MIGRATION.md` if the public or wire contract changes. |

## Phase 0 - Freeze the contract and control

- [x] Read the mapped source/tests, benchmark README, serialization/publication
  guides and latest `TRB-Benchmark-005` evidence. Record current source-stack
  revisions; retain the synchronized fixture and its exact cadence/replay.
- [x] Add bounded characterization tests for default/zero intervals, initial
  checks, retries, skipped/early-return heading updates and direct versus
  guide-backed restore. Existing behavior should pass before adding the feature.
- [x] Settle one minimal public configuration shape, including disabled versus
  phase zero, interval/phase validation, atomic replacement and enabling/disabling
  during an active request. Do not leave two competing configuration surfaces.
- [x] Record a finite state/transition table for due calculation, missed slots,
  forced checks, held actions, lifecycle reset and signed-counter boundaries.
  Define what survives save/load and how re-entry into another clock is handled.
- [x] Decide exact serialized keys and Navigator schema handling. Preserve
  transactional rejection of unsupported shapes; no silent compatibility shim.
- [x] Have an independent reviewer challenge correctness and unnecessary state.
  Resolve findings here before runtime implementation. Freeze test expectations
  and the distinct benchmark identities; no further planning document is needed.

**Exit:** Approved exact API, timing and record contracts, passing default-mode
characterizations, and an unchanged synchronized control. Do not mark this exit
complete merely because the design has been written down.

## Phase 1 - Implement and harden shared periodic timing

- [x] Write failing phase-enabled behavior tests, then implement the smallest
  bounded due-state calculation and shared periodic gate. Preserve required
  validation/retry order and avoid steady-state allocation.
- [x] Cover intervals 1, 2 and 16, phase boundaries and invalid combinations;
  starts on/off a slot; repeated heading calls; unchanged/default/zero behavior;
  runtime enable/disable/reconfiguration; and bounded delay after a forced check.
- [x] Exercise A* and Flow on blocked and clear routes, with actual movement and
  guide/direct-mode changes at the expected frames. Include medium mismatch,
  exhausted budgets, stale status and movement-group participation.
- [x] Prove relevant map/policy/overlay changes and profile replacement retain
  their existing invalidation/recovery boundaries even off-phase. Include a
  change immediately before a scheduled slot and a forced retry on that slot.
- [x] Cover paused/disabled movement, skipped updates, early returns, held and
  completed transitions, stop/start, arrival, reset, rebind and disposal. Verify
  exactly-once action ownership and no catch-up loop for missed refreshes.
- [x] Implement JSON and standard-package MemoryPack records together with the
  runtime state. Test enabled/disabled, direct/guided, same/new-clock restores,
  malformed late fields and unsupported schemas. Failed loads must preserve
  live guidance, pending actions, occupancy and timing state.
- [x] Replay stable host-assigned phases across fresh worlds, different object
  construction orders with the same simulation order, and restored sessions.
  Test signed-counter boundaries through existing internal clock test access or
  the bounded due-state calculation; do not add a public test-only clock API.
- [x] Run focused Release/ReleaseLean tests and allocation gates. Obtain an
  independent correctness/Ponytail review before timed evidence is accepted.

**Exit:** Both algorithms use the reviewed policy; default behavior is intact;
timing, lifecycle and wire semantics are covered. There is no new queue,
registry, unbounded catch-up work, or claim that the performance target is met.

## Phase 2 - Prove the containing-frame benefit

- [ ] Add distinct phased cases using existing benchmark host/helpers. If a
  small fixture extraction is needed, prove the original workload, setup,
  timing brackets and replay remain unchanged before comparing results.
- [ ] Include synchronized control, evenly distributed explicit phases and a
  deliberately unbalanced assignment. Assert exact periodic check frames/counts
  on the stable blocked workload so a lower frequency cannot masquerade as a
  win. Keep mandatory checks distinguishable from periodic checks.
- [ ] Record mode, interval, assignments, clock origin and per-frame refresh
  counts alongside the existing ticks, allocations, collection masks and hashes.
  Keep diagnostic instrumentation bounded and equivalent between compared cases;
  do not add a public telemetry API for this benchmark.
- [ ] Extend the existing analyzer and its self-tests for distinct mode/case
  identities and per-mode replay. Reject malformed/incomplete captures; never
  pool phased records with the old synchronized series or infer all expensive
  frames from a fixed modulo-16 index.
- [ ] Preflight pause/resume, simultaneous new requests, relevant invalidation,
  profile replacement and restore bursts. Time representative containing frames
  separately: initial acquisition is outside the original steady-state timer,
  so that control cannot establish startup or recovery costs.
- [ ] Size cases first, then freeze a bounded repeated protocol and the minimum
  peak reduction / tolerated full-block regression for a keep decision, before
  examining candidate timings. Keep the existing budget targets unchanged. Start with
  A*/100, Flow/100 and Flow/500; label any A*/500 sizing separately. Use matched
  runtime settings and interleaved control/candidate captures, with no concurrent
  builds, tests or profilers. Do not keep extending runs to hunt a favorable tail.
- [ ] Retain all launch/stage records and the executed child binaries, generated
  sources, runtime settings, SHA256 manifests and exact source revisions/diffs.
  Require complete successful captures, semantic preflight, deterministic
  per-mode replay and zero warmed current-thread frame allocation.
- [ ] Compare per-launch/full-block cost, observed frame P95/P99/max and counts
  over 2 ms and 31.25 ms. Show periodic density and startup/recovery bursts
  separately. Report total-work changes, phase-entry checks and shifted work;
  do not claim a universal frame-time ceiling from balanced agent counts.

**Exit:** Repeated full-frame evidence establishes whether phase spreading is
worth retaining. A keep decision needs a material, repeatable reduction of the
synchronized peaks without an unexplained full-block/default-mode regression.
If the effect is weak or complexity outweighs it, record a no-change decision
instead of continuing speculative optimization.

`TRB-Benchmark-005` is **not automatically closed** by completing this feature.
The 2 ms allowance and 31.25 ms full-frame budget remain visible. If either
still misses, record the remaining workload and an explicit follow-up or
reviewed disposition; do not redefine the target after seeing results.

## Phase 3 - Documentation, verification and closeout

- [ ] Update evergreen docs/XML with the actual API, default behavior, stable
  host phase assignment, fixed-frame order, pause/retry and restore semantics.
  Clearly separate LOS refresh latency from guide freshness and collision work.
  Compile/run any example presented as runnable C#; label partial host snippets.
  Do not link feature-work plans from wiki pages.
- [ ] Document any intentional public/wire changes in migration guidance; remove
  obsolete timing descriptions rather than leaving contradictory examples.
- [ ] Run the complete local source-stack Release/ReleaseLean matrix, both
  library targets, exact reachable coverage, replay/allocation gates, analyzer
  self-tests and DocFX warnings-as-errors. Include the Gravitas adapter tests;
  no adapter runtime change is implied by this plan.
- [ ] Obtain independent final correctness and Ponytail reviews. Resolve actual
  defects in the issue tracker and keep unrelated benchmark exceptions separate
  from accepted timing evidence.
- [ ] Summarize selected capture results and limitations here, update the
  benchmark signal and overview, then mark/move this plan to `done` only after
  its local gates or explicit no-change disposition are complete. Condense any
  supporting notes into this plan; keep raw captures in ignored artifacts.
- [ ] Report changed repositories and concise suggested commit messages. Do not
  stage, commit, push, tag or release without the corresponding request.

**Exit:** One documented timing contract, reviewed code and test evidence, an
honest benchmark disposition, and a closed local plan. Windows/Linux CI and
released-package verification remain the overview's coordinated release gates,
not checks implicitly satisfied by local measurements.

## Verification commands

These use existing entry points. Run focused tests first; exact new test/case
names are finalized in Phase 0. Repeat configuration-specific outputs into
separate artifact folders, and do not run coverage or builds during capture.

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj -c Release -p:UseLocalLsfStack=true --filter 'FullyQualifiedName~NavSteering|FullyQualifiedName~NavigatorSerialization|FullyQualifiedName~NavigatorProfileReplacement|FullyQualifiedName~NavigationTransitionSimulation'
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj -c ReleaseLean -p:UseLocalLsfStack=true --filter 'FullyQualifiedName~NavSteering|FullyQualifiedName~NavigatorSerialization|FullyQualifiedName~NavigatorProfileReplacement|FullyQualifiedName~NavigationTransitionSimulation'
dotnet build Trailblazer.slnx -c Release -p:UseLocalLsfStack=true
dotnet test Trailblazer.slnx -c Release -p:UseLocalLsfStack=true --no-build --settings tests/Trailblazer.Tests/coverlet.runsettings --results-directory artifacts/los-phase/coverage-release
dotnet build Trailblazer.slnx -c ReleaseLean -p:UseLocalLsfStack=true
dotnet test Trailblazer.slnx -c ReleaseLean -p:UseLocalLsfStack=true --no-build --settings tests/Trailblazer.Tests/coverlet.runsettings --results-directory artifacts/los-phase/coverage-lean
& tests/Trailblazer.Benchmarks/TestGuidedFrameAnalysis.ps1
dotnet tool restore
dotnet tool run docfx docs/api/docfx.json --warningsAsErrors
```

Use the existing CI ReportGenerator/exact-summary checks for each configuration:
covered lines equal coverable lines, covered branches equal total branches, and
both covered and fully covered methods equal total methods. Reuse the
[benchmark README's capture protocol](../../tests/Trailblazer.Benchmarks/README.md#complete-guided-host-frames)
for the original control; record the new phased selection and executed commands
here when available, rather than documenting a command for an API not yet built.

## Execution record

- 2026-09-14: Created this draft from the completed bounded micro-optimization
  investigation and reviewed source boundaries. No runtime implementation,
  new timing measurements, coverage run or release gate is claimed by drafting
  the plan. All execution phases remain unchecked.

### Phase 0 - Reviewed contract decisions

Execution began on 2026-09-14 from `5367f5a`. The clean local Release baseline
passes 2,505 core and 65 adapter tests. Local source-stack revisions are
FixedMathSharp `29bfa28`, SwiftCollections `4b5715b`, GridForge `52e9d94`, and
Gravitas `42af506`. The earlier benchmark record remains historical evidence;
the new feature has not yet been measured.

Independent source/contract review confirms these implementation decisions:

- One mutation API: `NavSteering.ConfigurePathRechecks(int intervalFrames,
  int? phase = null)`. `PathRecheckCooldownFrames` becomes read-only and
  `PathRecheckPhase` exposes the nullable phase. There is no parallel writable
  field/property or compatibility wrapper. This is an intentional source change
  with a migration example. Disabled mode accepts the original integer interval
  domain; enabled mode requires a positive interval and `0 <= phase < interval`.
- Identical configuration is a no-op. Validate the whole pair before changing
  anything. For active sessions preserve already-due work; otherwise align the
  next enabled slot, or start the ordinary countdown when disabling. New/idle
  sessions have no timing debt. Configuration is not a path request.
- The signed public `FrameCount` cannot distinguish long gaps, a complete
  counter wrap and reset. Use one internal nonnegative `ulong` frame index plus
  a reset-lifetime identity in the existing clock, with internal context access.
  Preserve public `FrameCount`'s low-32-bit behavior; expose no new public clock
  API or agent registry. Exhaustion rejects before mutating the clock. Reset
  replaces the lifetime identity and starts the index at zero.
- Keep only an armed frame index, a bounded positive delay and the owning
  lifetime reference for phased due state. For logical frame `T`, calculate
  `distance = P - (T % I)` and add `I` when non-positive. Due means elapsed
  frames reach that distance; do not add a future absolute deadline that can
  overflow. The existing countdown's zero value can preserve already-due work
  during active configuration changes, including enabling at logical frame zero
  after enough completed headings. Normal enabled arms clear that marker and
  enabled headings do not decrement it. Future-slot arms always use `1..I`. A missing/lost
  lifetime is unarmed, not proof that travel is valid.
- Before consuming an enabled session after a clock-lifetime change, discard
  its timing/direct-travel assumption and enter the existing re-admission path.
  Normal admission and retry paths remain outside the periodic gate. A check
  arms a strictly future slot, preventing a duplicate periodic check that tick.
- Keep the existing countdown semantics when disabled. Enabled stop, new query,
  reset and rebind discard phased timing debt. Profile replacement retains the
  configuration and existing forced re-admission. Held actions retain their
  current validation/completion boundary; missed periodic work is handled only
  when heading updates resume.
- Preserve record key `PathRecheckCooldownFrames`, add nullable
  `PathRecheckPhase`, and advance the outer Navigator schema from **5 to 6**.
  Path-session schema remains 1. Stage/validate the pair before mutation.
  Enabled restore clears old direct-travel trust and requires fresh admission
  at the next eligible update, in either the same or a new context. That check
  re-establishes the phase. Do not serialize phased clock identity or deadline;
  disabled restore retains its existing countdown/direct-mode behavior.

The clock/context files join the implementation map solely for the internal
index/lifetime boundary. Characterization and runtime tests must prove the
public signed wrap, reset-to-the-same-frame case and transactional counter
exhaustion. This avoids an undocumented maximum pause length or phase drift
after a 32-bit counter revolution.

Boundary verification found a separate existing world limit, now tracked as
`TRB-Issue-124`: publication accepts monotonically increasing signed frames and
rejects the public counter's signed wrap. The new LOS arithmetic is tested through
the internal clock boundary; it does not claim whole-world wrap support or change
publication ordering. The clock's own `ulong` exhaustion rejection is distinct
from transactional exhaustion of an entire world simulation frame.

Five new real-graph default-mode characterizations pass in Release, with four
in ReleaseLean (the MemoryPack case is omitted there). Existing tests cover
zero-interval, initial admission, stale retry, JSON direct restore and
guide-backed restore; their locations and focused commands are recorded in the
ignored task report. Independent spec/correctness/Ponytail review found no
actionable findings. No production change was needed for these characterizations.

The fresh unchanged `original-control` capture completes all three selected
cases and nine child exits: nine warmup, 27 actual and nine diagnostic records.
Its 1,728 actual frames allocate zero current-thread bytes; replay is exact
within/across launches. Actual-frame P95/max are 20.65/22.35 ms for A*/100,
11.50/15.82 ms for Flow/100 and 47.19/63.81 ms for Flow/500. These are whole
distribution statistics, not the earlier periodic-only medians. All executed
child binaries, generated source and SHA256 hashes are frozen under
`artifacts/los-phase/original-control-children`; logs and analyzed records use
the same `original-control` prefix. Runtime source still matched the branch base
when this capture ran.

The same actual-only baseline has 576 frames per selected case:

| Original control | Median 64-frame sum | Observed P99 | Frames >2 ms | Frames >31.25 ms |
| --- | ---: | ---: | ---: | ---: |
| A*/100 | 213.97 ms | 21.26 ms | 566/576 | 0/576 |
| Flow/100 | 100.28 ms | 14.90 ms | 36/576 | 0/576 |
| Flow/500 | 526.79 ms | 62.08 ms | 576/576 | 36/576 |

The block sum contains only the 64 inner host-frame timings, not untimed setup
or export, nor the semantic validation included in the outer BenchmarkDotNet
operation.

### Predeclared performance decision

The original `Simulate64GuidedFrames` method/case identity remains the
uninstrumented control. New matched-instrumentation method identities are
`Simulate64GuidedFramesInstrumentedUnphased`,
`Simulate64GuidedFramesInstrumentedBalancedPhases`, and
`Simulate64GuidedFramesInstrumentedUnbalancedPhase`. Their interval is 16;
assignments are null, stable host ordinal modulo 16, and all phase zero.
Independent benchmark design review supports observing the existing immediate
ray work meter in this sequential blocked-route fixture, with explicit rejection
of guide-recovery work and exact per-frame/total-count assertions. This is
benchmark-only evidence, not a general ray invocation counter or runtime API.

Before observing candidate timings, require balanced Flow/500 actual-frame P95
to improve by at least **50%** against the matched instrumented synchronized
control in each repeated capture. Require no more than **10%** full-block
median regression for each selected case; also compare the unchanged default
control against its frozen baseline and report per-launch differences. Report
P99/max and every budget miss, not only these keep criteria. The existing 2 ms
and 31.25 ms targets are not replaced by relative improvement thresholds.

Use bounded sizing followed by two matched repeated comparisons of A*/100,
Flow/100 and Flow/500 (three launches, one warmup, three actual 64-frame blocks
per case). Retain an unbalanced assignment as a bounded behavior/sizing control,
not an unrequested multiplication of the full repeated matrix. Separate
startup/resume/invalidation/restore burst evidence from steady-state blocks.
After sizing and the unchanged-method candidate control, capture the matched
instrumented modes in `Unphased-1 / Balanced-1 / Balanced-2 / Unphased-2` order
on the same candidate source. Archive each executed child immediately; no
concurrent builds, tests or profiling. A source correction requires a separately
labeled recapture, never silently mixing attempts.

### Phase 1 - Reviewed runtime and lifecycle implementation

Shared A*/Flow timing, the explicit configuration API, internal logical clock
and schema 6 records are implemented. Required admission remains outside phase
delay; disabled completed-heading behavior is preserved. Enabled trust-loss
boundaries set the existing due-now marker before later configuration can arm
a future slot. The final implementation removes the redundant direct-only
helper and unreachable lifetime guards rather than manufacturing coverage tests.

Independent review found and verified fixes for `TRB-Issue-126/127/128`: reset
trust loss, direct-only admission across lifecycle/reconfiguration, and legacy
initial-Stale cadence. Observable tests also establish exact transition/medium
timing, overdue pause handling, stale-policy recovery, late-load transactionality,
resource release/rebind, deterministic actual-movement replay and zero warmed
allocation. Clock-boundary headings use internal clock access; the separate
whole-world publication limit remains `TRB-Issue-124`.

Final runtime gates: **2,566 core + 65 adapter** tests in Release and **2,496 + 65**
in ReleaseLean, no failures or skips. Both library targets build without warnings
or errors. Each configuration covers exactly **31,017/31,017 lines,
12,179/12,179 branches and 3,032/3,032 covered and fully covered methods** across
the core and adapter assemblies. Evidence is retained under
`artifacts/los-phase/verification-round3`; independent spec and quality review
pass after the scoped fixes. This is not yet performance acceptance or final
documentation validation.

Actual-movement testing also exposed the pre-existing `TRB-Issue-125`: a valid
zero-radius body reaches an eager reciprocal in local group steering. Resolve
that narrow defect with its own regression/review before candidate capture;
keep its change separate from LOS timing and remeasure the unchanged control.
