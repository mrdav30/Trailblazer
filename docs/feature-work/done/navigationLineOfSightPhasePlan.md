# Deterministic Line-of-Sight Phase Spreading Implementation Plan

> **Execution record:** The user approved implementation on a feature branch.
> Keep the contract, decisions and condensed evidence here; raw captures remain
> in ignored artifacts. Phase checkboxes record verified progress.

**Created:** 2026-09-14

**Subsequent benchmark disposition (2026-09-20):** The user closed
[TRB-Benchmark-005](../benchmark-signal-hardening-backlog.md#trb-benchmark-005---guided-frames-spend-most-time-in-long-blocked-segment-tracing)
as an accepted stopping point, with the unmet 2 ms target explicitly deferred.
This supersedes the historical directions below to resume or keep that campaign
open; measurements, exceptions, unresolved failures and release gates are unchanged.

**Status:** Complete locally on 2026-09-14. All four phases, independent reviews,
performance acceptance and exact local verification gates pass. Coordinated
release verification remains separate.

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
[Timing contract](#timing-contract) define the intended
behavior. Phase 0 freezes exact signatures, state transitions and wire fields
before production changes. Any material departure returns here for review.

## Why this work comes next

The bounded micro-optimization sweep for
[`TRB-Benchmark-005`](../benchmark-signal-hardening-backlog.md#trb-benchmark-005---guided-frames-spend-most-time-in-long-blocked-segment-tracing)
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
  an owning-library fix. Capture defects in [issue-tracker.md](../issue-tracker.md).
  Releases remain deferred under the [overview](../feature-work-overview.md).
- Work on the requested `codex/los-phase-spreading` feature branch from
  `develop`, preserving unrelated worktree changes. Pushes, merges and releases
  still require the corresponding request.

### Not included

Lower refresh frequency, adaptive distance/visibility heuristics, sharing LOS
results between agents, resumable individual rays, staggered initial path
acquisition, or a new geometry/cache optimization pass. The existing scheduled
guide-acquisition service remains separate and does not own these refreshes.
Engine-specific visual playtesting and competitor benchmarking are not gates.

## Timing contract

Phase 0 froze these semantics and Phase 1 implemented them. One unphased default
remains; the changed timing is explicit when a host opts in.

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
check in frame 0. These are timing examples, not complete C# programs.

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

- [x] Add distinct phased cases using existing benchmark host/helpers. If a
  small fixture extraction is needed, prove the original workload, setup,
  timing brackets and replay remain unchanged before comparing results.
- [x] Include synchronized control, evenly distributed explicit phases and a
  deliberately unbalanced assignment. Assert exact periodic check frames/counts
  on the stable blocked workload so a lower frequency cannot masquerade as a
  win. Keep mandatory checks distinguishable from periodic checks.
- [x] Record mode, interval, assignments, clock origin and per-frame refresh
  counts alongside the existing ticks, allocations, collection masks and hashes.
  Keep diagnostic instrumentation bounded and equivalent between compared cases;
  do not add a public telemetry API for this benchmark.
- [x] Extend the existing analyzer and its self-tests for distinct mode/case
  identities and per-mode replay. Reject malformed/incomplete captures; never
  pool phased records with the old synchronized series or infer all expensive
  frames from a fixed modulo-16 index.
- [x] Preflight pause/resume, simultaneous new requests, relevant invalidation,
  profile replacement and restore bursts. Time representative containing frames
  separately: initial acquisition is outside the original steady-state timer,
  so that control cannot establish startup or recovery costs.
- [x] Size cases first, then freeze a bounded repeated protocol and the minimum
  peak reduction / tolerated full-block regression for a keep decision, before
  examining candidate timings. Keep the existing budget targets unchanged. Start with
  A*/100, Flow/100 and Flow/500; label any A*/500 sizing separately. Use matched
  runtime settings and interleaved control/candidate captures, with no concurrent
  builds, tests or profilers. Do not keep extending runs to hunt a favorable tail.
- [x] Retain all launch/stage records and the executed child binaries, generated
  sources, runtime settings, SHA256 manifests and exact source revisions/diffs.
  Require complete successful captures, semantic preflight, deterministic
  per-mode replay and zero warmed current-thread frame allocation.
- [x] Compare per-launch/full-block cost, observed frame P95/P99/max and counts
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

- [x] Update evergreen docs/XML with the actual API, default behavior, stable
  host phase assignment, fixed-frame order, pause/retry and restore semantics.
  Clearly separate LOS refresh latency from guide freshness and collision work.
  Compile/run any example presented as runnable C#; label partial host snippets.
  Do not link feature-work plans from wiki pages.
- [x] Document any intentional public/wire changes in migration guidance; remove
  obsolete timing descriptions rather than leaving contradictory examples.
- [x] Run the complete local source-stack Release/ReleaseLean matrix, both
  library targets, exact reachable coverage, replay/allocation gates, analyzer
  self-tests and DocFX warnings-as-errors. Include the Gravitas adapter tests;
  no adapter runtime change is implied by this plan.
- [x] Obtain independent final correctness and Ponytail reviews. Resolve actual
  defects in the issue tracker and keep unrelated benchmark exceptions separate
  from accepted timing evidence.
- [x] Summarize selected capture results and limitations here, update the
  benchmark signal and overview, then mark/move this plan to `done` only after
  its local gates or explicit no-change disposition are complete. Condense any
  supporting notes into this plan; keep raw captures in ignored artifacts.
- [x] Report changed repositories and concise suggested commit messages. Do not
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
foreach ($configuration in @('Release', 'ReleaseLean')) {
    dotnet build Trailblazer.slnx -c $configuration -p:UseLocalLsfStack=true
    foreach ($project in @('Trailblazer.Tests', 'Trailblazer.Gravitas.Tests')) {
        dotnet test "tests/$project/$project.csproj" -c $configuration --no-build -p:UseLocalLsfStack=true --settings "tests/$project/coverlet.runsettings" --results-directory "artifacts/los-phase/coverage/$configuration/$project"
    }
}
& tests/Trailblazer.Benchmarks/TestGuidedFrameAnalysis.ps1
dotnet tool restore
dotnet tool run docfx docs/api/docfx.json --warningsAsErrors
```

Use the existing CI ReportGenerator/exact-summary checks for each configuration:
covered lines equal coverable lines, covered branches equal total branches, and
both covered and fully covered methods equal total methods. Reuse the
[benchmark README's capture protocol](../../../tests/Trailblazer.Benchmarks/README.md#complete-guided-host-frames)
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
zero-radius body reaches an eager reciprocal in local group steering. The narrow
fix is independently reviewed: guard exactly zero group radius, preserving
positive arithmetic and avoidance. Two regressions fail before the fix and pass
afterward. Full Release **2,568 + 65** and Lean **2,498 + 65** tests pass with
exact **31,019 lines, 12,181 branches and 3,032 covered/fully covered methods**
in each configuration. Evidence is under `artifacts/los-phase/issue125` and
`verification-issue125`. Candidate capture will remeasure the unchanged control;
the adjacent runtime/load tuning asymmetry remains separately tracked as
`TRB-Issue-129`, without broadening this numerical fix.

### Phase 2 - Reviewed measurement boundary

The original benchmark identity and uninstrumented simulation loop remain.
Distinct Unphased, BalancedPhases and UnbalancedPhase cases use equivalent
bounded observations. Each agent's actual preparation/measured check schedule
and four checks per measured block are verified outside the timer; aggregate
frame counts remain the compact exported data. One shared post-frame helper
preserves semantic/hash validation across both loops.

The analyzer preserves old captures while validating method/mode identities,
per-sample frequencies, exact Int64 replay, complete case/launch/stage/exit
records, and separate nested-frame/block/outer-operation statistics. Independent
review identified and verified fixes for `TRB-Issue-130/131/132/133`. A temporary
equal-population observation swap fails despite unchanged fleet totals; it is
removed from the final fixture. Original/Unphased setup runs from copied output
outside the repository, without an ignored capture dependency. Frozen replay
comparison remains an explicit acceptance-analysis gate.

The bounded Flow/100 recovery probe passes initial admission, 17 ineligible
frames, one overdue resume and realignment, same-map rebake, profile replacement,
and JSON restore. Publication returns Applied with 99 replacement guides and
one off-phase zero heading; the next frame performs one retry plus six due
checks and restores 100 guides. The JSON preparation timer explicitly includes
serialization, source Navigator reset, target construction/preparation,
population and assertions; it is not a population-only timing.

Release benchmark build and parser checks pass. Dry unbalanced sizing
inadvertently selected all four parameterizations, including A*/500, because
separate CLI filters are unioned; those observations are not acceptance data.
Repeated captures retain the predeclared three-case selection using method and
case in each filter. Raw smoke/RED/GREEN evidence is under
`artifacts/los-phase/phase2`. No timing keep decision is claimed yet.

### Phase 2 - Accepted containing-frame comparison

**Decision: keep opt-in balanced phase spreading.** Both repeated comparisons
meet the predeclared relative gates. Flow/500 actual-frame P95 decreases by
**80.84% and 81.47%**, exceeding the required 50% in each pair. Its nested
64-frame block median changes by **+2.98% and -1.07%**; every selected case stays
within the 10% block-regression limit. This establishes peak spreading, not a
general reduction in total work or a portable frame-time guarantee.

The unchanged-method candidate control ran first, followed by
`Unphased-1 / Balanced-1 / Balanced-2 / Unphased-2`. Each capture selected only
A*/100, Flow/100 and Flow/500, with three launches, one warmup and three actual
64-frame blocks per case. Every capture has nine successful child exits,
nine warmup, 27 actual and nine diagnostic records. All 10,368 actual frames
across the six original/candidate captures allocate zero caller-thread bytes
and overlap no recorded GC collection. Preparation, export and post-frame
validation are outside those measured brackets.

| Pair | Case | Unphased block median | Balanced block median | Block change | Unphased P95 | Balanced P95 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | A*/100 | 206.96 ms | 203.55 ms | -1.65% | 19.94 ms | 3.69 ms |
| 1 | Flow/100 | 104.70 ms | 99.52 ms | -4.95% | 12.15 ms | 3.14 ms |
| 1 | Flow/500 | 506.15 ms | 521.23 ms | +2.98% | 45.48 ms | 8.71 ms |
| 2 | A*/100 | 213.17 ms | 204.64 ms | -4.00% | 20.50 ms | 3.55 ms |
| 2 | Flow/100 | 106.25 ms | 98.01 ms | -7.76% | 11.83 ms | 3.04 ms |
| 2 | Flow/500 | 537.63 ms | 531.85 ms | -1.07% | 47.41 ms | 8.79 ms |

Per-launch nested-block medians remain visible rather than being pooled away:

| Case / mode | Launch medians, pair 1 | Launch medians, pair 2 |
| --- | --- | --- |
| A*/100 Unphased | 204.44 / 206.96 / 208.52 ms | 213.17 / 266.59 / 209.95 ms |
| A*/100 Balanced | 202.06 / 202.34 / 215.06 ms | 204.31 / 206.16 / 204.01 ms |
| Flow/100 Unphased | 104.70 / 122.17 / 104.41 ms | 106.25 / 132.04 / 102.15 ms |
| Flow/100 Balanced | 99.79 / 97.24 / 99.52 ms | 99.40 / 96.93 / 98.01 ms |
| Flow/500 Unphased | 505.57 / 505.47 / 513.54 ms | 536.80 / 535.42 / 539.40 ms |
| Flow/500 Balanced | 520.48 / 521.61 / 521.23 ms | 526.45 / 521.86 / 538.05 ms |

Original-method candidate block medians differ from the frozen baseline by
**-5.23% / +5.23% / -3.42%** for A*/100, Flow/100 and Flow/500. The corresponding
ordinal launch changes are **-2.69/-2.14/-5.37%**, **+5.23/+5.66/+5.46%** and
**-4.04/-0.82/-4.71%**. These are descriptive serial comparisons, not evidence of
a default-mode speedup or an attributed runtime/JIT effect. Default replay is
exactly preserved; the modest Flow/100 increase is not hidden.

### Absolute targets, phase-entry work and bursts

Each measured agent still performs exactly four periodic checks per block:
400 checks for 100 agents and 2,000 for 500. Balanced slots contain 6-7 and
31-32 checks respectively. Its 32-frame preparation contains 294/1,469 checks
including mandatory admission, versus 200/1,000 unphased; these extra initial
phase-entry opportunities are not presented as reduced work. The unbalanced
control concentrates checks in a single slot and does not establish spreading.

| Balanced capture | Case | P99 | Maximum | Frames >2 ms | Frames >31.25 ms |
| --- | --- | ---: | ---: | ---: | ---: |
| 1 | A*/100 | 3.88 ms | 5.17 ms | 576/576 | 0/576 |
| 1 | Flow/100 | 3.37 ms | 3.58 ms | 161/576 | 0/576 |
| 1 | Flow/500 | 9.15 ms | 10.15 ms | 576/576 | 0/576 |
| 2 | A*/100 | 3.76 ms | 4.39 ms | 576/576 | 0/576 |
| 2 | Flow/100 | 3.18 ms | 3.29 ms | 159/576 | 0/576 |
| 2 | Flow/500 | 9.25 ms | 10.23 ms | 576/576 | 0/576 |

Unphased P99/max values are **20.63/21.29**, **16.02/16.83**, **61.12/62.71 ms**
in pair 1 and **21.65/23.23**, **16.27/18.21**, **63.09/64.61 ms** in pair 2.
Its >2 ms counts are **526/36/576** then **576/73/576**; only Flow/500 exceeds
31.25 ms, in **36/576 frames each time**. Spreading peaks can increase the count
of smaller allowance misses. The 2 ms allowance remains unmet.

A separate bounded Flow/100 probe executes the archived candidate binary. The
following are single same-host observations, not repeated BDN distributions;
milliseconds use the recorded host stopwatch frequency of 10,000,000 ticks/s,
also verified against all candidate guided-frame payloads:

| Boundary | Observed time | Allocated bytes | Direct checks / guides |
| --- | ---: | ---: | --- |
| Initial admission, cold guide payloads and warm ray workspace | 496.87 ms | 772,560 | 100 / 100 |
| Resume after 17 ineligible frames | 17.45 ms | 0 | 100 / 100 |
| Same-map rebake publication | 277.15 ms | 1,105,152 | 100 / 99 |
| Next pending retry plus normal phase | 230.74 ms | 771,568 | 7 / 100 |
| Public profile replacement admission | 246.36 ms | 771,568 | 100 / 100 |
| End-to-end JSON restore preparation, not a simulation frame | 206.62 ms | 19,592,968 | 0 / 0 |
| Restored next admission | 20.61 ms | 21,600 | 100 / 100 |

Paused frames perform no checks; the 16 frames after overdue resume perform
100 checks total, with no catch-up loop. JSON preparation overlaps a collection
and includes serialization, source reset, shell construction, preparation,
population and assertions. Required acquisition/recovery still creates large
bursts; this feature does not spread that work or restore a guide during load.

**`TRB-Benchmark-005` remains active.** The steady 31.25 ms peaks are resolved for
these balanced cases, but the 2 ms allowance and synchronous recovery costs are
not. A next bounded pass should isolate ordinary guide sampling/body validation
and controller work, or explicitly address recovery-acquisition ownership, before
proposing a structural change. Do not resume an open-ended blocked-ray sweep,
lower the refresh frequency, or enlarge the budget to manufacture acceptance.

### Provenance and final local verification

Candidate source is `9f56efe`, with the same clean sibling revisions recorded
above. Capture-time edits were Markdown only. Independent audit parses all 270
raw records, reproduces every relative gate and budget count, verifies 17,280
exact Int64 replay comparisons, and rehashes all 378 manifest files across six
archives. Eight candidate assembly identities match across all five candidate
captures. Executed generated runners, source and runtime settings are retained;
the archive chain is not a signed provenance attestation or rebuild proof.

Raw evidence is under `artifacts/los-phase`: `original-control`,
`candidate-control`, the four ABBA prefixes and their `-children` directories,
`comparison.json`, and `frozen-lifecycle*`. The original capture is reparsed
without replacement as `phase2/original-control-reparsed.json`. The one sizing
wrapper's post-capture PowerShell expected-count arithmetic was corrected and
the original records revalidated; no runtime, sample or accepted protocol was
changed. Failed implementation/parser experiments remain separate from captures.

Final Release passes **2,572 core + 65 adapter** tests; ReleaseLean passes
**2,500 + 65**, no failures or skips. Both library targets build with zero
warnings/errors. Each configuration covers exactly **31,019/31,019 lines,
12,181/12,181 branches and 3,032/3,032 covered and fully covered methods**.
The parser suite passes. DocFX builds with zero warnings/errors; the four wiki
helper tests pass. All three current configuration host fragments appear
verbatim in the executed console harness and pass phase/default/opt-in checks.
Documentation review is independently approved. Coverage and logs are under
`final-verified-replay`, `docfx-final.log`, `doc-snippet-final.log`, and the wiki/link
logs. A missing namespace import in the temporary verification harness was
corrected without changing the authored fragments.

Final whole-branch review identified and resolved a replay-evidence gap
(`TRB-Issue-134`). Actual reversed construction now retains explicit logical
IDs, host phases and simulation order; equivalent restored runs compare twelve
per-frame movement/LOS/guidance snapshots across multiple phase slots. A*/Flow
JSON and standard-package MemoryPack cases pass. Ponytail's duplicate timing
fixture finding is also resolved: all five characterizations share the existing
partial fixture. Scoped re-review approves both fixes with no remaining findings.
The fix is tests-only; no runtime defect or benchmark recapture is claimed.
The full matrix was rerun after this fix and retains the exact coverage above;
the analyzer self-tests also pass. All local phases are now closed, with
supporting results condensed into this plan. Raw captures and review records
remain under ignored `artifacts/los-phase`.
Windows/Linux CI and released-package validation remain deferred coordinated
release gates; no sibling repository, remote branch, tag or release was changed.

### Local handoff

Only Trailblazer changed. Reviewed implementation, documentation and final test
proofs are committed locally on `codex/los-phase-spreading` through `ce09901`;
the subsequent documentation closeout archives this plan. The branch remains
available for the user's final review and optional squash into `develop`.
Suggested squash message: `feat(steering): add deterministic LOS phase spreading`.

`TRB-Benchmark-005` remains open for its 2 ms allowance and mandatory recovery
costs. The separately documented signed world-frame limit (`TRB-Issue-124`),
runtime/restored steering-range asymmetry (`TRB-Issue-129`) and historical
benchmark exception (`TRB-Issue-119`) are not claimed fixed by this feature.
