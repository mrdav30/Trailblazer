# Issue Tracker

## Tracker Rules

- Issue IDs use `TRB-Issue-NNN`. The next available ID is `TRB-Issue-156`.
- Assign an ID when an issue enters this tracker, keep it through resolution,
  and never reuse an ID even if an entry is later removed. Check this file's Git
  history before advancing or repairing the counter.
- Add new items when feature work uncovers a pre-existing bug, stale doc, test
  smell, performance anomaly, or correctness risk outside that feature's own
  implementation. Track regressions introduced on a feature branch in its active
  plan, including their fixes and verification; do not defer them into this
  tracker.
- Keep each item scoped tightly enough to fix and verify independently.
- Record the date on the item, not in this filename.
- Move an item to `Resolved Issues` only after the fix has tests or documented
  verification evidence.
- Use `Closed Investigations` for documented decisions to stop without a
  confirmed fix. Preserve the evidence, limitations and reopening criteria.
- Do not use this tracker as a substitute for tests, benchmarks, or release
  notes.
- Performance issues should stay in
  [`benchmark-signal-hardening-backlog.md`](benchmark-signal-hardening-backlog.md)
  unless they become a confirmed runtime defect. Do not add performance issues
  here until they have been investigated and confirmed as runtime defects.

## Active Issues

### TRB-Issue-119 - Guided A\* setup reported an exception in wide geometry

- **Discovered:** 2026-09-10 during guided-host performance measurement.
- **Status:** Open. One historical BenchmarkDotNet A\*100 setup failure has not
  reproduced in the bounded follow-ups below. No fix or runtime attribution is
  claimed.
- **Failure:** `NullReferenceException` in
  `WideConvex2dRelations.GetRotatedOffset` at ProjectionMath.cs:477, reached
  from `ContainsPoint` through GridForge's planar segment interval calculation
  and Trailblazer's A* simplification. The fixture fails during untimed
  `Setup -> Prepare -> Navigator.Simulate -> RequestGuide`; no A* timing result
  exists for that failed launch.
- **Boundary:** The new wall-detour fixture permits 1,024 simplification rays;
  historical cold A\* cases disable simplification. The reported helper and its
  inspected arithmetic inputs are value types, with no obvious nullable-object
  dereference. Optimized source mapping, code generation, provenance and other
  corruption are hypotheses, not established causes. Do not add a null guard,
  swallow/retry the exception, or disable simplification as a purported fix.
- **Provenance:** Trailblazer `4a02250` plus the new benchmark-only fixture;
  GridForge `7179cce`, SwiftCollections `4b5715b`, local-stack Release, .NET
  8.0.29. Retained failure stack: `artifacts/benchmark004/guided-sizing-2.log`.
  The generated child, source, runtime configuration and dependencies are frozen
  in `failed-guided-child`, with `failed-guided-child-hashes.json`. The passing
  standalone run is `astar100-probe.log`; it changes the hosting harness and
  does not erase the failed BenchmarkDotNet evidence. `guided-baseline.log`
  retains the three subsequent passing launches with their original preparation
  order; their nine actual blocks pass replay and lifetime checks. The
  pre-optimization benchmark binaries are separately frozen in
  `guided-baseline-bin`. The optimized candidate subsequently passes another
  three A\* launches with identical captured replay hashes. This does not
  identify or fix the earlier exception: the performance change avoids some
  disjoint-candidate math calls.
- **Follow-up watch (2026-09-11 UTC):** Four frozen generated-runner A*/100
  controls complete without the exception (two per GridForge version, same
  shared runtime). Fresh parent-driven guided baseline and
  FixedMathSharp-candidate captures each pass all three A* launches. Logs and
  executed-child snapshots are under `artifacts/benchmark005`. Historical
  parent-output hashes are not a substitute for the executed child's binaries;
  new captures preserve both the actual child runtime and generated source.
  These clean runs are additional evidence, not a fix or a reason to discard the
  original failure. The containment optimization removes a redundant query-point
  transform; polygon transforms still use the reported helper. Neither fewer
  calls nor successful runs establish the cause of the original exception.
- **Separate later failure:** The next tracing slice's unchanged-runtime
  pointy-hex strict-miss microbenchmark reports a null-reference exception in
  generated `WorkloadActionUnroll` after 13 actual iterations. GridForge tracks
  it as `GF-Issue-006`, with the actual child/source/hash snapshot under
  `artifacts/benchmark005/planar-edge-baseline-children`. This is not an A\*
  setup failure; its normal path exits before containment. Neither a shared
  cause nor a relationship to the original wide-helper stack has been
  established.
- **Upstream investigation (2026-09-11):** `GF-Issue-006` remains open after
  seven additional unchanged-child probes: one console-host replay and six
  acknowledged-pipe replays, three with first-chance capture attached. All exit
  zero; none is a replay of this A\* setup failure or a repair of the incomplete
  planar baseline. Evidence and a validated native-fault capture procedure are
  retained upstream and locally under `artifacts/gf-issue006`. A separate null
  control showed that name-only exception collection missed a caught NRE;
  including native access violations captured the pre-managed fault state.
  Neither the control dump nor these passing probes establishes a common cause.
- **Per-edge experiment watch:** Fresh `guided-ray-{baseline,candidate}`
  captures each complete three A*/100 launches, plus the full Flow/100 and
  Flow/500 matrix, with exact cross-version replay. These six clean A* launches
  do not establish a fix. The GridForge edge guard was withdrawn after mixed
  performance results; no new runtime workaround is retained.
- **Next check:** If this setup failure recurs, capture the preparation frame,
  agent index and exact simplification ray/prism inputs before reducing it to
  the public math boundary. Repeated clean runs are not a root-cause diagnosis.
  Preserve pre-change artifacts when testing optimizations; avoiding calls on
  disjoint candidates must not be described as a fix for this exception. This is
  distinct from the closed `TRB-Benchmark-004` protocol watch and does not meet
  `TRB-Issue-114`'s allocation-guard reopening criteria.
- **Separate containment micro failure (2026-09-13 UTC):** FixedMathSharp now
  tracks `FMS-Issue-018`: two unchanged-runtime
  `Outside(4 vertices, 30 degrees)` captures fail in launch 2. The first exits
  with native access violation `0xC0000005` after six actual iterations, without
  a managed stack; the second reports an NRE in `GetWorldPoint` after four.
  Windows event/log and executed- child archives are preserved under
  `artifacts/benchmark005/fms-vertex-baseline*`. Both incomplete captures are
  excluded from comparison. Neither is an A* setup failure, and no common cause
  or fix is established. The subsequent guided baseline/candidate each complete
  all three A* launches with matching replay; endpoint reuse removes the
  reported helper from this containment path, so clean candidate runs cannot
  establish a fix.

## Closed Investigations

### TRB-Issue-114 - Intermittent allocation signal in the covered warm Flow attach guard

- **Discovered:** 2026-09-08
- **Area:** `NavigationFlowFieldSamplingConcurrencyTests` allocation guardrails
- **Status:** Closed on 2026-09-10 by maintainer decision; investigation
  inconclusive, with no confirmed production defect or allocator fix.
  Warmup/measurement consistency has been improved, but the allocation source
  has not been attributed. Passing runs are not a root-cause resolution.
- **Evidence:** The initial Phase 3 full covered `Release` run passed 2,394
  tests and failed `WarmAttachSampleDispose_ShouldAllocateZeroBytes` with 616
  bytes instead of zero. The unchanged test then passed alone (1/1) and with its
  class (9/9). The measured allocator has not been attributed; no runtime
  allocation defect or 16-cycle storage-growth threshold has been established.
- **Earlier mitigation:** Aligned warmup with the adjacent guards at 256
  iterations. The
  [acquisition cost audit](done/navigationSchedulingPlan.md#issue-114---separate-allocation-guard-investigation)
  records the earlier passing matrices and separates this covered
  acquire/attach/sample/dispose signal from acquisition-only timing.
- **2026-09-10 investigation:** Temporary preallocated phase counters reproduced
  7,552 bytes in one sample after 16 warmups, 5,192 bytes in a sample after 256
  warmups, and 5,640 bytes in acquisition after 16 warmups. The bursts did not
  identify a stable allocation site. These diagnostic builds changed code
  layout; they do not establish that the same allocator produced the historical
  616 bytes, or that the unmodified 256-warmup guard has the same failure.
  Source review found the warmed payload/guide/graph-lease pools already primed,
  with no ordinary per-cycle construction on the successful path. No constructor
  or exhausted pool was tied to a failing window.
- **Attribution limits:** Fully decoded small-allocation traces retained six
  measured windows without lost events, but all six measured zero bytes. Seven
  full covered runs with caller-thread JIT counters also passed; the three
  traced counter runs recorded no caller-thread compilation in the measured
  phases. Three tiering-disabled runs passed, but this does not establish
  tiering as the cause. Independent .NET-only probes measured 8.4 million lock
  windows with allocation churn and forced/concurrent collections without a
  nonzero delta. Neither a runtime-accounting defect nor a JIT allocation has
  been established.
- **Guard improvement:** Warmup and measurement now call the same assertion-free
  helper, kept separate from the test by `NoInlining`. Both still use 256
  iterations. Successful sampling, drained leases and exactly zero measured
  bytes remain mandatory; there are no retries, tolerances or runtime-setting
  changes. The separate first-attach guard is unchanged. All temporary
  production probes and tracing hooks were removed. This is a test-harness
  improvement, not evidence that the historical allocator was fixed.
- **Verification:** Four fresh covered core `Release` runs passed 2,455/2,455
  each after the extraction. A temporary 64-byte array allocation on every
  iteration made the exact guard fail at 22,528 bytes; that mutation was
  removed. The final local-source matrices passed `Release` (2,455 core + 65
  adapter) and `ReleaseLean` (2,393 core + 65 adapter), with zero build
  warnings/errors. Both merged reports have exact coverage: 30,841/30,841 lines,
  12,087/12,087 branches and 3,009/3,009 fully covered methods. These results
  verify the guard's sensitivity and current gates, not allocation attribution.
- **Closure decision:** Stop the investigation rather than continue speculative
  changes or repeated clean runs without an actionable allocation site. Retain
  the strict zero-allocation guard and this evidence record. No further work is
  scheduled for this issue, and it no longer blocks the measured performance
  pass. The completed `TRB-Benchmark-001` timing attribution is unchanged.
- **Reopen when:** The retained guard fails again on an otherwise unmodified
  production build, or new evidence identifies a concrete allocating branch or
  allocation type/stack. Preserve the failing commit, configuration, runtime,
  coverage settings and logs so follow-up starts from new evidence rather than
  repeating this investigation. Closure does not waive future allocation
  failures.

## Resolved Issues

### TRB-Issue-155 - The final allowed jump cancels its held-input extension

- **Discovered:** 2026-09-23 during shared timing adoption.
- **Resolved:** 2026-09-24; new-jump admission no longer cancels an active hold.
- **Original evidence:** `ResolveTraversalForces` cleared `IsHoldingJump` when
  `CanJump` became false. Registering the last allowed jump exhausts its count
  and starts cooldown, so the default single jump lost its extension on the
  next held frame. The guard predates the timing migration at `e2a9edf`;
  `TRB-Issue-153` corrected a separate elapsed-time units defect.
- **Reproduction:** The default jump lost one gravity step from
  `4.427188724279404` to `4.12093872576952`. The new rational-step regressions
  fail before the fix with `4 -> 3.75` for a single jump and `8 -> 7.75` for the
  final double jump. All seven initial regression cases fail on the old guard,
  including zero, short and century-scale world uptimes. Original timing logs
  remain in Chronicler's ignored `artifacts/timing/phase5/red-motor*.log`;
  this fix's evidence is under Trailblazer's ignored `artifacts/trb-issue155`.
- **Fix:** Only released input clears the hold in `ResolveTraversalForces`.
  New-jump eligibility remains enforced by `CanApplyJumpForce`; count,
  cooldown and affordability do not gate continuation of an admitted jump.
  Release/repress cannot revive that hold without a new admission. Existing
  hold-window, ceiling, landing and locomotion cancellation remain in place.
  Input stays level-triggered; serialization and public API shapes are unchanged.
- **Regression coverage:** Exact accepted velocity/displacement for single and
  final multi-jumps, affordability changes, cooldown expiry without count
  replenishment, release/repress, inclusive hold-window boundaries and changing
  timestep; finite tap/hold arcs and landing events; ceiling cancellation;
  landing with a still-active cooldown; real Gravitas prepare/resolve/commit
  motion. Duplicate and misleading height tests were replaced, including a
  purported release test that actually held input. Height settings tune the
  discrete arc rather than impose an exact continuous-height clamp.
- **Verification:** Independent Windows and Linux source builds and full suites
  pass with `UseLocalLsfStack=true`: Release has 3,245 core + 90 adapter tests;
  ReleaseLean has 3,146 + 86. Each of the four runs retains exact coverage across
  both assemblies: 32,475/32,475 lines, 13,290/13,290 branches, and 3,190/3,190
  covered and fully covered methods. Both target frameworks build without
  warnings or errors; DocFX passes with warnings as errors. Independent source
  review found no actionable issues. No upstream changes or release actions.

### TRB-Issue-124 - World publication cannot advance across signed frame-count wrap

- **Discovered:** 2026-09-14 while testing LOS phase clock boundaries.
- **Resolved:** 2026-09-23 through shared Chronicler timing adoption.
- **Original evidence:** `TrailblazerWorldContext.Simulate()` advanced the clock
  before passing signed `FrameCount` to graph maintenance. Publication requires
  increasing, nonnegative frames, so int wrap rejected after time had already
  changed. Both guards existed at `5367f5a`; a separate unsigned LOS timeline
  did not repair world publication. At 32 Hz the limit was about 2.1 simulated
  years, potentially reached sooner by accelerated simulation.
- **Reproduction:** At `fd6b484`, two one-rule policies effective at int.MaxValue
  with dependency work limited to five leave the second pending. The next public
  `Simulate()` throws after wrapping. The zero-origin control succeeds. Phase 0
  probes and Phase 5 red/green output remain in Chronicler's ignored timing
  artifacts; permanent `TrailblazerTimingBoundaryTests` reproduce the containing
  world call, not just an internal clock increment.
- **Fix:** One ChronicleClock supplies long frames and wide elapsed time.
  Publication, receipts, group history, LOS, scheduler attempts/grants, motor
  transactions and adapter/committed-cell stamps use that timeline. Nonnegative
  and monotonic admission guards remain intact; receipt publication stays volatile.
- **Verification:** Pending work publishes on exact long frames across the old
  limit; future work waits and late/regressing work rejects. Frame/time exhaustion
  leaves clock state, hooks, pending policies and scheduled ownership unchanged.
  Real two-actor adapter motion, LOS phases, group expiry, scheduled adoption and
  reset-to-the-same-frame rejection pass. Independent Windows/Linux builds and
  full suites pass in Release and ReleaseLean. Both Windows package
  configurations retain exact core/adapter line, branch and fully covered
  method coverage.

### TRB-Issue-154 - Stale controller commits could mutate motion before rejecting

- **Discovered and resolved:** 2026-09-23 during shared timing adoption.
- **Evidence:** At `e2a9edf`, motor traversal validates only the numeric frame;
  prepare at zero, reset the context, and commit at zero accepts stale work.
  Navigator updates position, occupancy and velocity before motor finalization,
  so an ordinary next-frame rejection also leaves partial changes. The Gravitas
  adapter's prepared-frame guard likewise does not distinguish a reset lifetime.
- **Fix:** Pending motor transactions retain the context's process-local clock
  identity. Core and adapter preflight it before committing accepted state.
  Hosts explicitly abort/reset stale work; identity is never serialized.
- **Verification:** Owning regressions first failed for motor reset and for
  core/adapter mutation, then passed. They verify unchanged pose/velocity/events
  on rejection and preserve the normal exactly-once adapter barrier.

### TRB-Issue-153 - Held-jump duration mixed absolute time with height

- **Discovered and resolved:** 2026-09-23 during shared timing adoption.
- **Evidence:** The old `(JumpStartTime + ExtraJumpHeight) / jumpSpeed` threshold
  mixed seconds and world units. The same held jump passed at world time zero
  but lost gravity compensation at ten seconds and one million seconds. Tests
  initially used a second available jump to isolate this from `TRB-Issue-155`;
  they now exercise the default single jump after that separate fix.
- **Fix:** Compare the wide elapsed duration since jump start with
  `ExtraJumpHeight / jumpSpeed`. Zero speed or non-positive extra height gives
  no extension; expired durations need not fit Fixed64.
- **Verification:** Identical early/100-year jump traces, future/start/exact-end/
  expired/100-year-old windows, and zero-speed/height controls pass. JSON and
  MemoryPack preserve the exact wide timestamp and reject retired timing records
  without changing live Navigator state.

### TRB-Issue-129 - Runtime steering ranges bypass restored-value validation

- **Discovered:** 2026-09-14; **resolved locally:** 2026-09-22.
- **Cause:** Public mutable `NavSteering.GroupFactor` and `AvoidFactor` fields
  admitted negative runtime values that Navigator's nested-load validation
  rejected. Direct steering-record population also bypassed that validation.
- **Fix:** Same-named non-negative properties reject before mutation through
  `SwiftThrowHelper`. Steering records stage and validate both factors before
  applying either; Navigator retains its full staged-load guarantee. Defaults
  remain 10/3, zero remains valid, and wire keys/order/schema are unchanged.
  `ComputeCombinedSteering` rejects negative body radii even when stopped, so
  the signed-radius scan fallback and its compatibility tests are removed.
  No silent clamping, wrappers, or general tuning redesign was introduced.
- **Compatibility:** Normal assignment and initializer syntax is unchanged.
  Field-specific references/reflection need migration and compiled consumers
  need rebuilding; see [steering range validation](../MIGRATION.md#steering-range-validation).
- **Regression evidence:** Before the fix, 12 new cases failed because negative
  assignments, direct radius arguments, and standalone JSON/MemoryPack loads
  were accepted. The replacement tests cover rejection without factor mutation,
  stopped/moving callers, zero and positive grouping/avoidance, valid round
  trips, omitted defaults, and transactional Navigator rejection.
- **Validation:** Source-stack Windows Release passes 3,187 core + 84 adapter
  tests; ReleaseLean passes 3,096 + 80. Both configurations retain exact combined
  coverage for `Trailblazer` and `Trailblazer.Gravitas`: 32,459/32,459 lines,
  13,278/13,278 branches, and 3,188/3,188 covered and fully covered methods.
  The same four managed test suites pass under Linux/WSL with .NET 8.0.26
  (execution of the built assemblies, not a separate Linux build). Full solution
  builds pass both `netstandard2.1` and `net8.0` in Release and ReleaseLean;
  DocFX passes with warnings as errors. Independent correctness/simplicity
  review found no actionable issues.
  Raw red/focused/full-suite evidence and reports are under `artifacts/issue129`.

### TRB-Issue-152 - Legacy hex inverse disagrees with the quantized forward lattice

- **Discovered / resolved:** 2026-09-21 / 2026-09-22.
- **Owner:** GridForge `GF-Issue-010`; corrected in `315e0cc`.
- **Reproduction:** With pointy-hex radius six raw units and layer height one,
  address `(100,0,100)` projects to `(1500,0,900)` raw, but the old idealized
  inverse returned q approximately `94.333`, not `100`. Flat hexes failed
  symmetrically. The new planar trace had already avoided this mismatch through
  bounded searches over forward coordinates; legacy callers remained affected.
- **Fix:** Invert the actual quantized width and row step. Recognize exactly
  projected authored corners before outward dimension rounding. The shared
  lookup, snapping, dimension and candidate-range consumers now use the
  corrected conversion; no minimum radius or expanded search margin was added.
- **Verification:** Both orientations pass tiny-metric and distant signed-index
  round trips, authored/non-authored bounds normalization, dense lookup and
  coverage, and sparse stationary interval/3D-body endpoint checks. Full
  GridForge suites pass 1,162 tests in each configuration; downstream Gravitas,
  Trailblazer and adapter suites also pass against the source-built
  dependencies. See the
  [cross-stack verification record](twoDimensionalNavigationPlan.md#2026-09-22---upstream-contact-and-hex-defects-resolved).
- **Boundary:** The inverse requires unsaturated intermediate arithmetic.
  Previously affected inputs can produce corrected dimensions/configuration
  keys; rebuild derived data from authored inputs. Native planar tracing keeps
  its independent bounded forward searches. Released-package validation remains
  separate.

### TRB-Issue-151 - Generalized capsule contact classification loses exact corner touch

- **Discovered / resolved:** 2026-09-21 / 2026-09-22.
- **Owner:** FixedMathSharp `FMS-Issue-020`; corrected in `d53bf28`, with
  related side-contact witness correction in `da6b6f1`.
- **Reproduction:**
  `FixedSegment2d.TryGetCenteredCapsuleConvexMinimumTranslation` rejected a
  zero-axis capsule at `(3,4)`, radius `5`, against polygon
  `(-1,-1),(0,-1),(0,0),(-1,0)`, despite exact 3-4-5 corner tangency. Scales 1,
  10, 100 and 1000 reproduced. Rounded normalized axes were used for
  classification while assuming an exact unit radial extent.
- **Fix:** Retain exact unnormalized axes and radial extents through contact
  classification and minimum-depth ordering; round the selected output only
  afterward. Side contacts also project the opposing feature onto the capsule's
  finite side. This repairs the generalized solver, not merely the independent
  planar boolean predicate, and does not introduce an epsilon workaround.
- **Verification:** The original four scales now return contact with exactly
  zero depth. One-raw-unit separation/penetration, rotated nonzero-length
  capsules, full-domain cases, integer-distance oracle comparisons and contact
  witnesses pass. Gravitas collision, overlap and sweep consumers pass their
  owning regressions in both configurations. Full cross-stack suite counts and
  source-DLL checks are recorded in the
  [verification record](twoDimensionalNavigationPlan.md#2026-09-22---upstream-contact-and-hex-defects-resolved).
- **Boundary:** This closes the contact defect, not the remaining bounded
  support/posture implementation, native controller acceptance or
  package-release gates.

### TRB-Issue-150 - Gravitas2D exact-touch circle sweeps return the probe center as the surface point

- **Discovered / resolved:** 2026-09-21 during the native 2D navigation
  preflight.
- **Owner:** Gravitas `GRV-Issue-074`,
  `src/Gravitas/Queries/2D/QueryDetection2D.cs`, `TrySweepCircle`.
- **Reproduction:** At Gravitas `df1915142ec9c8c7937ee7aa8aa6d25a99cee4cd`, a
  pure TwoD bodyless AABB centered at `(0,-1/2)` with size `(12,1)` receives
  `SweepCircleAll` from `(0,1/2)` to `(0,0)`, radius `1/2`, all layers, no
  triggers. The hit has the correct collider, zero distance and `(0,1)` normal,
  but returns the probe center `(0,1/2)` instead of the floor point `(0,0)`.
  Both a landed circle and capsule lower-cap query reproduced the defect.
- **Fix:** Carry the overlap's actual rigid-frame target anchor into the sweep
  result, retaining zero travel and honest point representability. No
  Trailblazer workaround or radius subtraction was added. Regression work also
  corrected the related centerline fallback inconsistency in `GRV-Issue-075`.
- **Verification:** 32 new owning behavioral cases cover initial contact,
  penetration, ordinary sweeps, compound ties, scalar faces and rotated capsule
  centerlines. Full Gravitas Release/ReleaseLean tests and exact 100%
  line/branch/method coverage pass. The repeated native preflight now passes all
  52 checks with zero witness gaps. This resolves the query defect, not the
  remaining 2D geometry/controller gates or package-release verification.
- **Evidence:**
  [Original reproduction](twoDimensionalNavigationPlan.md#2026-09-21---source-development-and-native-2d-preflight)
  and
  [fix/slope follow-up](twoDimensionalNavigationPlan.md#2026-09-21---correct-witnesses-and-slope-anchor-proof).

### TRB-Issue-146 - Guided action approaches can oscillate across exact entry

- **Resolved:** 2026-09-20 after both package-family gates and independent
  review.
- **Discovered:** 2026-09-20, Gravitas integration Phase 2, before changing
  approach behavior.
- **Evidence:** Both A\* and Flow failed to expose the ladder instruction within
  512 real adapter frames. Ordinary arrival tolerance does not authorize an
  action. The earlier core ladder fixture masked crossing by replacing the
  accepted position with the exact entry.
- **Fix:** Explicit action-approach metadata reaches the motor, which bounds
  only controlled displacement at a crossing. Gravity, jumping, platform carry,
  and collision acceptance remain independent. The artificial core-fixture
  docking was removed. Both core and real-adapter ladder sequences pass,
  including blocked entry, retry, and exact action ownership.
- **Evidence record:**
  [Joint Phase 2 record](done/gravitasIntegrationPlan.md#2026-09-20---joint-movement-hardening).

### TRB-Issue-147 - Navigator restore discards pending and active turning

- **Resolved:** 2026-09-20 after both package-family gates and independent
  review.
- **Discovered:** 2026-09-20, changed-posture rotating-platform replay.
- **Evidence:** JSON and MemoryPack restore preserved the current quaternion but
  changed `TargetReached` from false to true. The resumed platform trace
  diverged because `NavTurning` recorded only configuration and Navigator load
  reinitialized its continuation.
- **Fix:** Navigator schema 8 records queued/active targets, interpolation, and
  the collision latch. Loading refreshes the profile radius without resetting
  continuation; callbacks/context stay shell-owned. Exact resumed platform and
  focused turn traces pass in both transports.

### TRB-Issue-148 - Adapter population leaves a discarded prepared frame live

- **Resolved:** 2026-09-20 after both package-family gates and independent
  review.
- **Discovered:** 2026-09-20, body-first Navigator restore between prepare and
  commit.
- **Evidence:** Base Navigator population aborted motor traversal and discarded
  queued motion, but the adapter retained its prepared-frame flag and request.
  The obsolete commit remained admissible and another prepare could be rejected.
- **Fix:** Successful population clears adapter-local contact and preparation
  state after base validation/commit. Failed population leaves the live prepared
  frame intact. Both outcomes have JSON/MemoryPack behavioral regressions. This
  is not whole-world rollback: hosts still restore authoritative physics/world
  state in order.

### TRB-Issue-149 - Slope direction is reversed and projected velocity is flipped

- **Resolved:** 2026-09-20 after both package-family gates and independent
  review.
- **Discovered:** 2026-09-20, real collision-accepted uphill/downhill walks.
- **Evidence:** On the plane `y = 3x/4`, the outward normal is `(-3/5,4/5,0)`.
  Positive X was classified downhill. A later Y-sign adjustment reversed the
  already-correct surface projection, so uphill requests entered the support and
  stalled; downhill requests moved away from it. Two historical test
  expectations encoded the same reversed directions.
- **Fix:** Classify horizontal travel from its exact normal projection and
  retain the surface-projected velocity without a second sign rewrite. Idle
  remains gravity-biased; contour travel retains the surface inclination for
  slope limits. Analytic sign tests and repeated real uphill/ downhill traces
  pass, including support-plane approach limiting.

All four fixes pass the final local-stack matrix: 3,164/3,081 core and 84/80
adapter tests in Release/ReleaseLean, with exact 100% line, branch, and fully
covered method coverage in both assemblies. The shared
[Phase 2 evidence record](done/gravitasIntegrationPlan.md#2026-09-20---joint-movement-hardening)
retains commands, traces, cross-library verification, and review findings.

### TRB-Issue-144 - Identical ordinary A\* cache hits dropped newly admitted endpoint dependencies

- **Discovered:** 2026-09-17 during Navigator recovery coverage verification.
- **Evidence:** Seed a nearest-endpoint A* route with only its selected map
  published, then publish an overlapping map over already-existing physical
  grids. The identical query/key still resolves to the same route, but its new
  admission reads the overlap too. An ordinary hit discarded those extra reads;
  removing that overlap left the returned public guide `Success` instead of
  `Stale`. Shared-cohort admission correctly retained its independent proof.
  This is distinct from Issue136's Flow defect and the prior A* characterization
  involving different strict/nearest query keys.
- **Resolution:** Ordinary A\* hits use the existing counted endpoint
  certificate comparison and, only when needed, reserved lease-local proof
  capture. Proof attachment and accounting use the cache lock; publication and
  guide freshness still validate both route and endpoint proofs. Certified hits
  retain no proof allocation, and no query budget, queue, or workspace is added.
- **Verification:** Public-status RED1/4 in
  `task-6-astar-overlap-status-red.log`; focused admission/scheduler GREEN133
  and proof/boundary/warm-allocation GREEN10 in `task-6-astar-proof-green.log`
  and `task-6-astar-proof-boundaries.log` under
  `.superpowers/sdd/navigationRecoveryPlan`. Controls cover ordinary/shared
  publication, exact304/one-below303 active proof bytes, cancellation during
  sliced capture, retained leader isolation, and existing zero-allocation warm
  public acquisition. Full final configuration/platform gates are separate.

### TRB-Issue-138 - Motion commit refreshed occupancy before host contact correction

- **Discovered:** 2026-09-17 during scheduled Flow recovery smoke.
- **Evidence:** At recovery frame 1242, agent 0 retained physical ticket
  `(2,0,2)` while its committed cell was `(2,0,3)`. Before host contact
  correction its root was `(2,-0.5095703124534339,2.5000000074505806)`; the
  final foot/root was `(2,-0.5,2.5000000074505806)`. It had usable guidance and
  was not waiting. The temporary below-prism position prevented occupancy
  admission before the host restored ground support. This predates scheduled
  acquisition.
- **Resolution:** `CommitFrameMotion` refreshes occupancy after
  `CheckTrekCondition`, matching the final position used for velocity and
  committed-cell resolution. Existing host overrides do not depend on
  intermediate occupancy.
- **Verification:**
  `CommitAfterHostGroundCorrection_ShouldMoveOccupancyWhileWaitingOrIdle` failed
  2/2 with the old cell ticket, then passed 2/2 after the ordering fix. Full
  recovery/core regression remains part of the feature acceptance gate.

### TRB-Issue-136 - Ordinary Flow cache hits discard follower endpoint proof

- **Discovered / resolved locally:** 2026-09-17 during frozen-cohort
  implementation.
- **Reproduction:**
  `SharedFlowEndpointProof_OverlapRemovalShouldInvalidateOnlyFollower` failed on
  ordinary checkout at publication, public conversion and later use: removing
  `a-overlap` left the nearest-origin lease incorrectly `Success`, while the
  strict zero-distance source payload on `z-candidate` remained legitimately
  current.
- **Fix:** Ordinary Flow hits and shared followers use one metered,
  version-aware endpoint-subset certificate. Covered admissions retain no new
  proof; extra dependencies retain their own fully reserved lease-local stamp.
  Publication/conversion/use reject follower-only staleness without invalidating
  the source. Source cloning preserves the proof; slot transfer balances the old
  and new charges. The scheduled-only stale-cache miss optimization remains
  scheduled-only; Immediate now fails safely for the defect.
- **Evidence:** The ordinary/shared overlap lifetime matrix is green, including
  unrelated publication and source clone/transfer.
  `CachedEndpointProof_ExactBytesAndCancellation_ShouldDrainOwnership` checks
  304-byte exact / 303-byte failure, cancellation, worker reuse and capacity-one
  sharing.
  `ScheduledCachedLeader_ExtraEndpointProof_ShouldSurviveCloneAndTransfer` uses
  real cached leader acquisition and verifies context reset during extra-proof
  capture. Certification tests check one-lookup slices, exact / one-below total
  budgets, component and page versions, policy and transition requirements. The
  corresponding A\* strict to nearest query builds a distinct payload because
  its key includes the full endpoint policy.
- **Limits:** Different replacement proofs can transiently require both old and
  new proof bytes. No generic retry, redundant cold build or source-proof index
  was added. Full-solution, exact coverage, DocFX, Linux and matched timing
  gates remain separate; the scoped report records the precise locally executed
  Release/Lean evidence.

### TRB-Issue-135 - Local-stack test hosts execute the released math dependency

- **Discovered / resolved:** 2026-09-15 during direct-visibility binary
  verification.
- **Cause:** The three executable test/benchmark hosts selected released math
  `010be577` through transitive package assets despite `UseLocalLsfStack=true`.
  Generated prebuilt benchmark children correctly selected sibling `29bfa28`.
  Both expose assembly version 7.1.0.0, but seven core source files differ;
  unlike the earlier provenance-only `TRB-Issue-116`, this affects validation.
- **Fix:** Exclude competing math compile/runtime package assets only in local
  host builds, with explicit sibling project references where missing. Standard
  and Lean families remain separate; prebuilt children and normal package-mode
  dependency selection are unchanged. No runtime source or copied-DLL
  workaround.
- **Verification:** Captured a failing three-host hash assertion before the fix.
  All local hosts now execute sibling revision `29bfa28`: Release SHA256
  `8B125CBF3E70BF6C3731638510A33AECA1F79C43EC990CDBA08BA03721F7A4AB`, Lean
  `BF21DA9A746A603E382D6C81E84E69210B85ED237017EDD8A84809E772FC9283`. Manifests
  select reference runtime assets; host traces resolve those DLLs. Full aligned
  solution builds and tests pass: Release 2,576 core / 65 adapter, Lean 2,504 /
  65, no failures/skips or build warnings; both library targets build. Commands:
  `dotnet build Trailblazer.slnx -c <configuration> -p:UseLocalLsfStack=true`,
  then
  `dotnet test Trailblazer.slnx -c <configuration> -p:UseLocalLsfStack=true --no-build --no-restore`.
  Both benchmark parents pass the 40-record lifecycle smoke. One generated
  Release child executed with the intended hash; both prebuilt families retain
  zero project/math package references. All six normal package-mode host builds
  preserve their released math hashes and package runtime entries. Independent
  correctness/minimality review found no remaining issues. Raw logs, manifests
  and the red/green hash check remain in ignored direct-visibility artifacts.
- **Boundary:** Earlier tests remain evidence only for the binaries they ran.
  The four calibration children were already aligned and unchanged, so their
  within-stack comparisons remain valid. This fix makes no performance claim.

### TRB-Issue-134 - Phased replay proof omits construction-order and restored sequences

- **Discovered / resolved locally:** 2026-09-14 during final LOS phase review.
- **Gap:** The movement replay constructed both runs in the same order. Enabled
  restore tests established admission/timing but not repeated multi-frame
  replay. The checked plan requirement overstated that evidence; no runtime
  defect was established.
- **Fix:** Extend the shared real-movement fixture with actual reversed
  construction and stable explicit logical IDs, phase assignments and simulation
  order. Equivalent restored executions reuse the same saved intent and compare
  twelve per-frame position/LOS/guidance snapshots across multiple phase slots.
  Restored runs compare with restored runs, not uninterrupted execution. The
  default-timing file now reuses the same partial fixture, removing copied
  setup/helpers while retaining all five characterizations.
- **Verification:** Focused Release passes 58/58 (six fresh/restored A\*/Flow
  replay rows); ReleaseLean passes 49/49 (four applicable rows). JSON is covered
  in both configurations and MemoryPack in Release. Independent scoped review
  approves both the proof and fixture cleanup. Evidence is in
  `artifacts/los-phase/final-fix`; production and benchmark source are
  unchanged.

### TRB-Issue-123 - Navigator migration and restore guidance disagree with current records

- **Discovered / resolved locally:** 2026-09-14 during LOS documentation work.
- **Failure:** Migration still named outer schema 4 when source/wiki used 5;
  restore prose implied unconditional fresh guidance despite the existing
  direct-mode/countdown behavior. A serialization-to-pathing anchor was stale.
- **Fix:** Document implemented outer schema 6 and nested schema 1, persisted
  configuration versus non-persisted runtime schedule, enabled fresh admission
  versus preserved unphased behavior, and the corrected evergreen link. The
  migration shows an unphased replacement before separately labeled phase
  opt-in.
- **Evidence:** Independent documentation review approves. Three current host
  fragments execute verbatim; DocFX builds with zero warnings/errors. Four wiki
  helper tests and changed-document/site link checks pass. Evidence is under
  `artifacts/los-phase/doc-snippet-final.log`, `docfx-final.log` and wiki/link
  logs. No default runtime behavior was changed to match the stale prose.

### TRB-Issue-133 - Candidate LOS evidence loses per-agent check identity

- **Discovered / resolved locally:** 2026-09-14, before accepted candidate
  timing.
- **Failure:** Fleet counts hid agent identity, so exchanged slots or missing
  work masked by another agent could pass. The instrumented loop also copied the
  original post-frame semantic/hash safeguards.
- **Fix:** Preallocated observations retain each agent's actual check.
  Validation outside the timer verifies assigned frames and four measured checks
  per agent. Both variants share the post-timer semantic/hash helper; the
  original simulation loop remains uninstrumented.
- **Evidence:** A temporary swap of equal-population observation slots leaves
  aggregate counts unchanged but fails at frame 12, agent 0. The injection was
  removed. Clean Release build, parser suite, balanced lifecycle and unbalanced
  dry cases pass; independent scoped review approves. Raw evidence is under
  `artifacts/los-phase/phase2/round1-*`.

### TRB-Issue-132 - Candidate benchmark requires an ignored local baseline

- **Discovered / resolved locally:** 2026-09-14, before accepted candidate
  timing.
- **Failure:** Original/Unphased setup required an ignored local control
  summary, making normal benchmark use fail on a clean checkout.
- **Fix:** Remove the capture-specific reader and ancestor probing. Reusable
  setup retains self-contained replay validation; explicit cross-capture
  analysis owns exact frozen-control comparison.
- **Evidence:** Copied Release output outside the repository runs exactly the
  original and Unphased A\*/100 cases successfully, with no baseline file to
  discover. Independent review approves;
  `round1-original-unphased-no-artifact.log` retains the run. This portability
  correction does not waive replay acceptance.

### TRB-Issue-131 - Candidate analyzer can accept an unfinished case or extra launch

- **Discovered / resolved locally:** 2026-09-14, before accepted candidate
  timing.
- **Failure:** Launchless case headers could disappear from completeness checks;
  extra or out-of-order launch indices could also pass.
- **Fix:** Register every declared case, reject repeated/unfinished headers, and
  require a unique exact `1..n` launch sequence with successful child exits.
- **Evidence:** Focused malformed fixtures fail before correction; the complete
  parser suite passes afterward and the historical control still reparses.
  `parser-completeness-red.log`, `parser-sequence-red.log` and
  `round1-parser-green.log` retain evidence. Independent review approves.

### TRB-Issue-130 - Candidate guided-frame analyzer pools unscaled ticks

- **Discovered / resolved locally:** 2026-09-14, before accepted candidate
  timing.
- **Failure:** Pooling raw ticks and applying the final sample's frequency
  distorted valid captures containing different stopwatch frequencies.
- **Fix:** Convert each sample with its own frequency before pooling. Exact
  Int64 replay comparison remains separate from timing statistics.
- **Evidence:** Two launches with equal real duration and different tick
  frequencies reproduce the defect, then pass. The full parser suite and frozen
  control reparse pass; independent review approves. Raw evidence:
  `artifacts/los-phase/phase2/parser-frequency-red.log` and
  `round1-parser-green.log`.

### TRB-Issue-125 - Local steering divides by zero for an admitted zero-radius body

- **Discovered / resolved locally:** 2026-09-14 during phased movement testing.
- **Failure:** At positive vessel speed, local steering eagerly calculated
  `1 / (radius * GroupFactor)`. Radius zero is a valid admitted body shape; zero
  `GroupFactor` reaches the same denominator. The defect predates `5367f5a`.
- **Fix:** A computed zero group radius uses a zero reciprocal. Group range
  already excludes all group neighbors there, while independent avoidance stays
  active. All nonzero arithmetic and evaluation order remain unchanged.
- **Evidence:** Both new tests fail with `DivideByZeroException` before the fix.
  One proves real zero-radius Navigator movement after the initial zero-speed
  frame; the other asserts exact avoidance with zero group factor. Independent
  spec/quality review passes. Full Release 2,568 core + 65 adapter and Lean
  2,498 + 65 pass, both targets build clean, and each configuration has exact
  31,019 lines, 12,181 branches and 3,032 covered/fully covered methods. Raw
  evidence: `artifacts/los-phase/issue125` and `verification-issue125`.
- **Separate follow-up:** `TRB-Issue-129` records runtime/load validation
  asymmetry; this fix does not redesign public tuning fields.

### TRB-Issue-128 - New-request initialization changes the legacy stale-ray cadence

- **Discovered / resolved locally:** 2026-09-14, before committing the LOS
  feature.
- **Failure:** Unconditionally clearing the countdown for a replacement query
  caused an extra same-heading ray when initial admission returned `Stale`.
- **Fix:** Only enabled new sessions clear timing debt. Disabled replacement
  preserves the previous completed-heading countdown.
- **Evidence:** A real-graph regression observes the exact later clear-path
  switch. Independent runtime review passes; Release 2,566 core + 65 adapter and
  Lean 2,496 + 65 pass with exact 100% line/branch/method coverage. Raw RED and
  final matrix logs remain under `artifacts/los-phase`.

### TRB-Issue-127 - Phasing prevents initial direct-only travel

- **Discovered / resolved locally:** 2026-09-14, before committing the LOS
  feature.
- **Failure:** Enabled `CanPathfind=false` requests could remain motionless or
  wait for a phase before initial admission. Reconfiguration could hide pending
  admission after new request, clock reset, successful restore or active rebind.
- **Fix:** Trust-loss boundaries establish the existing due-now marker before
  configuration can arm a future slot. Direct-only and graph-backed sessions
  share that invariant; no extra flag or special direct-only helper remains.
- **Evidence:** Ten direct-only lifecycle cases include JSON/MemoryPack restore,
  rebind and repeated reconfiguration. Tests observe immediate admission and the
  exact later phase, rejecting an every-heading workaround. Independent review
  passes; the full Release/Lean matrix and exact coverage cited above are green.

### TRB-Issue-126 - Reconfiguration can discard reset identity before invalidating direct travel

- **Discovered / resolved locally:** 2026-09-14, before committing the LOS
  feature.
- **Failure:** Disabling or repeatedly changing enabled phases after context
  reset could erase the old clock identity while retaining its direct-path
  trust.
- **Fix:** Invalidate direct travel and prepare admission when the lifetime
  change is detected, before replacing configuration or clearing its identity.
- **Evidence:** Both sequences fail before the fix and pass against a changed
  weighted map afterward. Independent runtime review and the full Release/Lean
  matrix pass with exact 100% coverage; no old direct trust is restored by
  timing.

### TRB-Issue-122 - Component dependency byte accounting omits the medium slot

- **Discovered:** 2026-09-14 during independent review of `TRB-Issue-121`.
- **Status:** Resolved locally on 2026-09-14.
- **Failure:** `GraphDependencyStamp` charged 32 bytes per component dependency,
  but the full address-medium key plus generation occupies 40 bytes in the
  supported x64 layout. This pre-existing mismatch undercounted both retained
  payloads and admission reservations by eight bytes per component.
- **Resolution:** Charge 40 bytes per component in the shared retained-byte
  formula, which feeds A* and Flow payload accounting. Raise the default single
  payload ceilings by 4,096 bytes each to cover their existing 512-component
  workspaces: A* is 1,163,576 bytes and Flow is 1,016,120 bytes. This restores
  the same admitted workspace envelope; custom host limits remain explicit. No
  new retained fields or runtime-dependent budget calculation are introduced.
- **Verification:** The existing payload-layout test now checks actual struct
  sizes and derives the dependency-array charge from those sizes. Before the
  fix, it expects 416 bytes for two components/four pages and observes 400;
  downstream A\* payload expectations increase by the same 16 bytes. The Flow
  exact-fit/one-byte-short test moves from 768/767 to 776/775 bytes for one
  component. The first full run exposes the stale default ceilings; correcting
  them restores the semantic-actions and culture-invariance tests without
  changing their budgets or canonical digests. All 11 focused fallout checks
  pass. The full Release/ReleaseLean matrix documented in `TRB-Issue-121` also
  passes; no budget assertions or determinism digests are relaxed. Evidence:
  `component-byte-accounting-red.log`, `continuation-final-verification` and
  `dependency-accounting-fallout-green.log` under `artifacts/benchmark005`.

### TRB-Issue-121 - Dependency workspace merges components from different media

- **Discovered:** 2026-09-14 during `TRB-Benchmark-005` source review.
- **Status:** Resolved locally on 2026-09-14.
- **Failure:** Component dependency accumulation deduplicated the representative
  cell address but omitted the traversal medium. Solid, Gas and Liquid
  components can share an address without sharing membership or generation.
  Recording one could silently discard the others, undercount capacity/merge
  work, and let a guide retain a stale component proof.
- **Reproduction:** A map has one Solid/Gas cell and a long Gas-only
  continuation. A short Solid-to-Gas A\* guide and bounded Flow field both
  incorrectly report `Success` after a distant Gas cell is suppressed. The Gas
  component changes, while the Solid component, local
  semantic/physical/transition page, transition rules and native world sequence
  remain unchanged. Eight focused regression cases fail on the original
  implementation, including both public guide checks.
- **Resolution:** A fixed-capacity generation-stamped set now uses the complete
  `NavigationSurfaceComponentKey` in recording, full-capacity duplicate checks
  and dependency-union preflight. Insertion order, lookup charging and atomic
  merge admission are unchanged. More distinct components correctly consume more
  capacity; no global graph invalidation or public API is introduced.
- **Verification:** All eight regression cases pass after the fix. Both local
  Release/ReleaseLean solution builds pass with zero warnings/errors, including
  `netstandard2.1` and `net8.0`. Core tests pass 2,501 / 2,439 cases, plus 65
  Gravitas-adapter cases per configuration: 5,070 total, no failures/skips. Both
  configurations retain exact configured coverage of 30,883/30,883 lines,
  12,109/12,109 branches and 3,017/3,017 fully covered methods, with unchanged
  exclusions. Independent correctness and Ponytail review finds no blocker.
  These are local Windows source-stack checks, not Linux CI or released-package
  validation. Evidence is under
  `artifacts/benchmark005/medium-dependency-red.log`,
  `medium-green-scalar-characterization.log` and
  `dependency-final-verification`; tests are in
  `NavigationDependencyMediumTests.cs`.

### TRB-Issue-120 - Benchmark launcher discards failure status

- **Discovered:** 2026-09-11 during upstream `GF-Issue-006` investigation.
- **Status:** Resolved locally on 2026-09-11; benchmark tooling only.
- **Failure:** Every launcher execution route discarded `BenchmarkSwitcher.Run`
  summaries and returned zero, making a failed benchmark appear successful to
  scripts. GridForge's matching defect is tracked as `GF-Issue-007`.
- **Resolution:** All routes return `1` for critical validation errors, failed
  reports/builds, missing executables and nonzero/unknown returned child exits.
  Canonical and single-shot groups both run; either failure survives the other
  group's success. Help/list still return `0`. Runtime navigation, dependencies,
  benchmark selection and measurement job policy are unchanged.
- **Verification (2026-09-11):** Temporary checks reproduced the original false
  success and verified later-launch/post-result failures, mixed reports and
  failure in either group while both still ran. Result-model checks covered
  validation/build failures and unknown exits. Local Windows `Release` and
  `ReleaseLean` builds passed without warnings/errors, with 2,467 core + 65
  adapter and 2,405 core + 65 adapter tests respectively. Production DLLs
  returned `1` for invalid invocation/unroll settings and `0` for help/list.
- **Scope (2026-09-12):** Retain the launcher fix and documented evidence only.
  The temporary verification projects and their CI additions were removed at
  maintainer review; no permanent benchmark-tooling test system is introduced.
- **Evidence location:** `artifacts/gf-issue006/launcher-red-trailblazer.log`,
  `artifacts/gf-issue006/launcher-validation/Trailblazer-*`, and the per-case
  logs/counters under `artifacts/launcher-regression/`.
- **Remaining limits:** BenchmarkDotNet 0.15.8 omits extra diagnoser executions
  from returned reports, and empty summaries also represent informational or
  invalid/no-match commands. Continue requiring complete expected child exits,
  launch/sample counts and semantic records from full logs. Neither a zero exit
  nor partial statistics makes an incomplete capture eligible for comparison.
  `GF-Issue-006` and `TRB-Issue-119` remain open; this is not an NRE fix.

### TRB-Issue-118 - Rebase/rejoin allocation guards use different warmup and measurement bodies

- **Discovered:** 2026-09-10 during `TRB-Benchmark-003` full validation.
- **Area:** `NavigationFlowFieldSamplingConcurrencyTests` test-harness
  consistency.
- **Status:** Resolved locally on 2026-09-10 for harness consistency only. Fresh
  Release (2,467 core + 65 adapter) and Lean (2,405 core + 65 adapter) matrices
  pass with exact 100% line/branch/method coverage. No runtime allocation defect
  or source for the observed burst has been established.
- **Evidence:** The first covered local-stack Release run passed 2,466 tests and
  failed `WarmExactNodeRebaseSample_ShouldAllocateZeroBytes` with 1,992 bytes
  instead of zero. The unchanged class then passed 9/9 in isolation. That pass
  is not evidence that the allocator was fixed. The failed binaries, hashes, TRX
  and coverage/logs are retained under `artifacts/benchmark003`.
- **Confirmed harness inconsistency:** Exact-node and rejoin guards warm with
  assertion-bearing `CreateGuide` loops, then measure separate unchecked loops.
  The neighboring attach guard already uses one shared, assertion-free,
  non-inlined helper. The two duplicate bodies now use that same existing helper
  with their original destination/displaced feet, budget and 256 iterations.
  Successful samples, fully drained leases and exactly zero bytes remain
  mandatory. No retries, tolerances, extra warmups, runtime settings, exclusions
  or production changes were introduced to address this failure.
- **Sensitivity:** The aligned class passes 9/9. Temporarily allocating a
  64-byte array each helper iteration makes the exact-node guard fail at 22,528
  bytes; the mutation was removed before final validation. Existing guide tests
  separately protect exact destination heading, rebase budget and displaced
  rejoin behavior. Independent review confirms that helper reuse removes no
  measured navigation work. A second fresh covered Release core confirmation
  also passes 2,467/2,467; all original failures remain retained.
- **Limits:** This resolves duplicated/inconsistent test setup, not allocation
  attribution or proof that flakiness is cured. `TRB-Issue-114` remains closed;
  its aligned attach guard did not fail here. Preserve any future failure with
  its exact build and coverage context; passing runs do not waive a red gate.

### TRB-Issue-117 - Saturated native center spacing can admit a crossing without a portal

- **Discovered:** 2026-09-08
- **Area:** native surface traversal at extreme GridForge coordinate spans
- **Status:** Resolved; forward/reverse regressions and both covered matrices
  pass.
- **Evidence:** An admitted rectangular binding with X bounds
  `[-1073741824, 1073741824]` and cell width `1073741824` has three addresses.
  The intermediate `index * spacing` saturates at the last address. Its center
  is one raw unit short of the expected spacing even though all final corner
  prisms are representable. The neighboring prisms overlap instead of sharing a
  face, so GridForge rejects portal compilation. The existing evaluator ignored
  that failure in Release and reported `Passable`.
- **Resolution:** Reuse rectangular native templates only for matching actual
  displacement. Other native edges use checked GridForge compilation, and actual
  portal profile resolution is authoritative. Compilation/profile failures
  return `Impassable`. Hex clipping remains on direct compilation; no upstream
  coordinate-generation change is included.
- **Verification:** The real published-world regression fails against both the
  original direct-compilation path and the initial unconditional reuse.
  Forward/reverse rejection now passes. Covered Release passes 2,405 core + 65
  adapter tests; ReleaseLean passes 2,343 core + 65 adapter tests, both with
  exact 100% line/branch/method coverage. See the
  [follow-up record](done/navigationSchedulingPlan.md#native-portal-and-warm-validation-follow-up).

### TRB-Issue-116 - Profiling provenance inferred dependency origins from one runtime fingerprint

- **Discovered:** 2026-09-08
- **Area:** acquisition investigation and diagnostic artifact identity
- **Status:** Resolved by explicit parent/probe versus measured-child
  provenance.
- **Evidence:** The temporary profiling probe copied the benchmark parent
  output, including released FixedMathSharp 7.1.0, while the BenchmarkDotNet
  child selects the sibling source build. A matching `Trailblazer.dll` hash
  alone did not establish the claimed build origin of the whole stack. Both
  FixedMathSharp assemblies report 7.1.0.0, but have different informational
  versions and hashes.
- **Resolution:** Corrected the
  [investigation record](done/navigationSchedulingPlan.md#source-and-diagnostic-boundary),
  recorded separate parent/child fingerprints, and qualified the historical
  baseline's missing complete binary manifest. The FixedMathSharp core source is
  unchanged between the two commits and all 3,570 normalized method bodies
  match; no different geometry implementation or runtime defect is inferred. The
  measured child matches the intended sibling output. No build-resolution change
  was mixed into the performance patch.

### TRB-Issue-115 - Release executes assertion-only surface geometry calculations

- **Discovered:** 2026-09-08
- **Area:** `NavigationSurfaceEdgeRouteWork.AdvanceSimple` and
  `TraversalEvaluator.EvaluateNative`
- **Status:** Resolved; compiled-call checks and behavior/coverage gates pass.
- **Evidence:** Investigation of `TRB-Benchmark-001` found two unconditional
  `IsNavigationBodySegmentValid` calls whose results feed only `Debug.Assert`.
  Release removed the assertion but still executed the calculations. Native
  retained-portal translation had the same assertion-only result pattern.
- **Impact:** Cold acquisition spent time on diagnostic results that did not
  affect Release passability, cost, route points or work counts.
- **Resolution:** Guarded the complete diagnostic calculations with `DEBUG`,
  preserving Debug's original assertion order. Runtime portal compilation,
  profile resolution and all five explicit-corridor segment rejection calls
  remain unchanged. The temporary compiled-call check fails before the patch and
  passes afterward in Release/ReleaseLean, with Debug diagnostics retained.
- **Verification:** Focused Release 191/191 and Debug 61/61; covered Release
  2,395 core + 65 adapter and ReleaseLean 2,333 core + 65 adapter pass. Both
  configurations retain exact 100% line/branch/method coverage. The
  [follow-up execution record](done/navigationSchedulingPlan.md#issue-115---diagnostic-work-removal)
  records the bounded remeasurement. The broader `TRB-Benchmark-001`
  subsequently closed with the completed scheduling scope and its explicit
  timing limits.

### TRB-Issue-113 - Benchmark backlog documented a nonexistent physics workload

- **Discovered:** 2026-09-08
- **Area:** benchmark reproduction guidance
- **Status:** Resolved; verified against the current benchmark catalog, CLI
  selection handling and local-stack builds.
- **Evidence:** The backlog instructed users to select
  `continuous-collision-evidence`, which is absent from Trailblazer's benchmark
  catalog, and recommended concave collision-shape authoring owned by Gravitas.
- **Resolution:** Replaced the stale commands with the actual benchmark catalog
  and public guide-service smoke selection, identified the unreleased
  local-stack build requirement, and removed the unrelated physics
  recommendation.

### TRB-Issue-112 - Parallel local-stack solution builds could race identical project outputs

- **Discovered:** 2026-09-08
- **Area:** local-source project graph and Trailblazer/adapter assembly identity
- **Status:** Resolved; verified by fresh parallel no-incremental `Release` and
  `ReleaseLean` solution builds.
- **Evidence:**
  `dotnet build Trailblazer.slnx -c Release -p:UseLocalLsfStack=true` scheduled
  `Trailblazer` and `Trailblazer.Gravitas` through direct solution entries and
  project references carrying different version global-property sets. Separate
  graph nodes then wrote the same `obj` path concurrently, producing a
  reproducible `MSB3101` cache-file warning and one observed `CS2012`
  assembly-file failure.
- **Impact:** A correct local-source tree could fail or produce a warning based
  on parallel scheduling. Serializing MSBuild avoided the race but concealed an
  incoherent project graph and weakened the documented zero-warning gate.
- **Expected:** Each in-repository project has one local-stack identity and one
  build node per target/configuration regardless of whether it is reached from
  the solution or a consumer project.
- **Resolution:** `Trailblazer.csproj` now owns the intended 2.0.0 local-stack
  version defaults when GitVersion or explicit version inputs are absent. The
  adapter and adapter-test references no longer inject redundant Trailblazer or
  adapter version globals. Fresh parallel no-incremental builds for both package
  configurations complete with zero warnings and errors, and the full solution
  tests retain their existing counts.

### TRB-Issue-111 - Same-lease Flow rejoin allocation gate did not establish its measured capacity state

- **Discovered:** 2026-09-08
- **Area:** Flow-field sampling zero-allocation regression coverage
- **Status:** Resolved; verified by repeated coverage-instrumented execution and
  the 2026-09-08 full package matrices.
- **Evidence:** `WarmSameLeaseRejoinSample_ShouldAllocateZeroBytes` performed
  only 16 setup iterations before measuring 256 attach/rejoin/sample/dispose
  cycles. A full coverage-instrumented run intermittently observed 1,888 bytes
  during the measured window even though the ordinary full suite and earlier
  coverage runs were green.
- **Impact:** The test could fail based on whether runtime specialization and
  retained capacity happened before or during measurement, obscuring a real
  hot-path allocation regression with a nondeterministic setup boundary.
- **Expected:** A zero-allocation steady-state assertion first exercises the
  same operation count as its measured window so all retained capacity and
  runtime paths are established outside the measurement.
- **Resolution:** The test now uses one named 256-iteration count for both setup
  and measurement, matching the adjacent exact-node rebase allocation gate.
  Repeated coverage-instrumented executions report zero measured bytes.

### TRB-Issue-110 - Physical notification failure could split an accepted adapter replacement across libraries

- **Discovered:** 2026-09-08
- **Area:** `GravitasNavigator3D` and the cross-library profile/collider
  replacement commit boundary
- **Status:** Resolved; verified by the 2026-09-08 focused and full package
  matrices.
- **Evidence:** Gravitas first moved separation callbacks after physical
  shape/root publication but still rethrew a callback failure before
  `Navigator.TryReplaceNavigationProfile(...)` could publish matching state.
  Returning that exception closed the interrupted commit, but independent review
  then proved the callback itself could still observe the new physical envelope
  alongside Trailblazer's previous profile, root, occupancy, and guidance.
- **Impact:** One accepted operation could leave the resized Gravitas body
  paired with Trailblazer's previous navigation envelope and lease state. A
  retry would be unsafe because the physical change had already committed.
- **Expected:** A physical transaction result and later host notifications are
  distinct. Both libraries finish their accepted state transition before any
  post-commit observer runs or a notification failure reaches the caller.
- **Resolution:** Gravitas now invokes an optional synchronized-state publisher
  after physical publication but before separation callbacks, retaining
  `Applied` as the transaction result. The adapter uses that publisher for the
  Navigator commit. Publisher and separation failures are returned through
  `notificationException`; Navigator rethrows them with preserved stacks only
  after all notifications finish. Core and cross-library regressions verify the
  synchronized state from inside the physical callback as well as afterward.

### TRB-Issue-109 - Profile replacement callback failure could skip a required capability-loss notification

- **Discovered:** 2026-09-08
- **Area:** Navigator runtime profile replacement and post-commit host callbacks
- **Status:** Resolved; verified by the 2026-09-08 focused and full package
  matrices.
- **Evidence:** An accepted replacement rebuilt committed-cell state before
  notifying the motor about lost traversal capabilities. If a host
  `CommittedCellChanged` callback threw after commitment, control escaped before
  an active climb could receive its required stop notification.
- **Impact:** The new profile, position, occupancy, and physical body were
  already committed, but host-observable capability-loss cleanup could be
  incomplete. Treating that exception as a rejected operation and retrying would
  then repeat an already accepted physical change.
- **Expected:** Replacement callbacks are a documented post-commit boundary. A
  committed-cell callback exception may propagate, but the climb-stop
  notification is still attempted and the caller must not retry the accepted
  replacement as though it had been rejected.
- **Resolution:** Capability-loss notification now runs from a `finally` around
  committed-cell publication. The regression deliberately throws from the cell
  callback and verifies the replacement state is committed, the climb is
  stopped, and both notifications were attempted.

### TRB-Issue-108 - Gravitas profile replacement did not reject a scaled physical owner before mutation

- **Discovered:** 2026-09-08
- **Area:** `GravitasNavigator3D` physical profile-replacement boundary
- **Status:** Resolved; verified by the 2026-09-08 focused and full package
  matrices.
- **Evidence:** Navigation profile dimensions are world-space values, while a
  Gravitas collider definition is interpreted through its owner's transform
  scale. The adapter originally forwarded a replacement without first proving
  exact unit world scale; a non-unit owner could therefore throw from the
  physical layer or represent an envelope different from the navigation profile.
- **Impact:** Expected physical-fit rejection could escape as an exception, and
  accepting scale-aware geometry without an explicit conversion contract would
  let collision and navigation clearance disagree.
- **Expected:** The focused capsule adapter accepts only a representable exact
  unit physical scale. Unsupported scale returns `PhysicalFitRejected` without
  changing the Navigator, body, or collider.
- **Resolution:** The adapter now preflights lossy world scale and rejects
  anything other than exact `Vector3d.One`. A regression verifies a scaled body
  preserves its original profile and physical state.

### TRB-Issue-107 - Group steering can erase a valid path heading indefinitely

- **Discovered:** 2026-09-06
- **Area:** NavSteering movement-group shaping and Flow-field progression
- **Status:** Resolved; verified by the 2026-09-06 focused and full package
  matrices.
- **Evidence:** In the Phase 1 off-axis Flow corridor, default separation and
  cohesion could exactly oppose the selected unit path heading for two actors at
  a stable non-overlapping spacing. The summed target became zero, so both
  Navigators remained guided without moving, arriving, or becoming formally
  stuck. Partial opposition also reduced the vector magnitude and therefore
  unintentionally acted as motor throttle.
- **Impact:** A legal Flow route could stall a movement group forever even
  though its graph proof, sampled portal heading, collision space, and per-frame
  budgets remained valid. Steering-force magnitude could also change traversal
  speed even though destination deceleration owns deliberate throttling.
- **Expected:** Treat group and avoidance output as directional shaping. Keep a
  nonzero shaped result normalized, and fall back to the selected path heading
  when local shaping cancels it exactly. Preserve `SetDeceleration` as the owner
  of deliberate path-speed reduction, while the collision host remains free to
  reject unsafe accepted motion.
- **Verification:** Focused steering regressions cover partial and exact
  opposition. The Gravitas-backed Flow scenario proves nonzero group influence,
  changed heading, bounded accepted displacement, no overlap-dependent start, no
  invalid/stuck events, and bounded arrival for both actors in `Release` and
  `ReleaseLean`. The complete instrumented matrices pass with exact reachable
  line, branch, method, and fully-covered-method totals in both configurations.

### TRB-Issue-106 - Surface guidance pitches the default facing direction vertically

- **Discovered:** 2026-09-06
- **Area:** Navigator default turning and accepted-motion collision turning
- **Status:** Resolved; verified by the 2026-09-06 focused and full package
  matrices.
- **Evidence:** The Phase 1 stair world produces legal guidance with an upward
  component. `Navigator.TryGetTurnDirection` previously forwarded that complete
  vector as the default facing direction, and accepted-motion turning likewise
  derived rotation from the complete vertical displacement. A world-Y-up body
  therefore pitched into a stair or slope even though the host had not supplied
  an explicit facing request.
- **Impact:** Ordinary surface locomotion could tilt a character's upright axis,
  distort its collision orientation, and make a valid rising route fail for a
  presentation choice that belongs to the host. The same behavior could appear
  for one frame after losing support during a surface route.
- **Expected:** Project only the implicit surface and falling facing direction
  onto the world X/Z plane. Preserve an explicit host-facing direction exactly,
  and retain three-dimensional default facing during active flight, climbing, or
  swimming. Apply the same ownership rule to accepted-motion turning.
- **Verification:** Focused Navigator and Gravitas collision-turning regressions
  cover grounded, newly airborne, explicit-facing, and volumetric locomotion.
  Transition-frame regressions also cover flight, climb, and swim admission;
  flight release; climb capability loss; and active swim-module removal, so
  facing dimensionality is selected from the resolved current-frame locomotion
  state. The complete instrumented `Release` and `ReleaseLean` matrices pass
  with exact reachable line, branch, method, and fully-covered-method totals.

### TRB-Issue-105 - Grounded motion discards legal upward surface guidance

- **Discovered:** 2026-09-06
- **Area:** NavMotor grounded velocity and Gravitas accepted-motion integration
- **Status:** Resolved; verified by the 2026-09-06 focused and full package
  matrices.
- **Evidence:** A four-map stair world publishes adjacent clearance prisms whose
  foot anchors rise by three quarters of a unit per step. Both A\* and Flow
  accept the profile and produce a forward/upward sampled heading. `NavMotor`
  then unconditionally clamps the grounded non-flight velocity Y component to
  zero, so Gravitas receives a horizontal request into the first riser and
  resolves the actor at the step face for every bounded frame.
- **Impact:** `NavigationAgentProfile.MaxStepUp` can admit a valid route that
  the standard Navigator/motor/host lifecycle cannot attempt. The same clamp
  also prevents a projected uphill surface velocity from reaching a collision
  host.
- **Expected:** Preserve the bounded upward component of an otherwise valid
  grounded surface-movement request so the host can accept or reject it against
  physical geometry. Pure vertical ground input must not become powered ascent,
  and accepted host motion remains authoritative. Prove the focused motor rule
  and complete the A\*/Flow stair world through real Gravitas collision/contact
  refresh before resolving this issue.
- **Verification:** Focused motor regressions preserve legal uphill surface
  velocity while rejecting flat-ground powered ascent. The Gravitas-backed
  four-map stair world accepts all three rises and reaches the exact upper
  address under A\* and Flow in `Release` and `ReleaseLean`. The complete
  instrumented matrices retain exact reachable coverage.

### TRB-Issue-104 - Falling has no fall-preserving controlled-descent contract

- **Discovered:** 2026-09-06
- **Area:** NavMotor airborne locomotion and fixed-frame host integration
- **Status:** Resolved; verified by the 2026-09-06 component, serialization,
  integration, and full package matrices.
- **Evidence:** The existing airborne choices are free fall and `FlyLocomotion`.
  Flight can limit downward speed, but admitting flight intentionally clears
  `FallLocomotion` state and permits powered ascent. The Phase 1 parachute-style
  scenario therefore cannot preserve fall distance, landing events, and a
  no-powered-ascent rule through the current public request contract.
- **Impact:** A host cannot request deterministic bounded descent while keeping
  the agent in the ordinary falling lifecycle. Reusing flight would report the
  wrong locomotion state and discard fall-height semantics.
- **Expected:** Add the smallest explicit controlled-descent intent to the
  required `FallLocomotion`: it activates only for a configured agent already
  falling in Gas, retains `IsFalling`, approaches a configured terminal descent
  speed with bounded deceleration, applies configured lateral control, and never
  creates upward velocity. Request cancellation resumes ordinary gravity,
  redeployment is allowed while still falling, and landing or disabling fall
  clears the transient state. Preserve deterministic cloning, reset,
  synchronization, JSON, and MemoryPack behavior.
- **Verification:** Component tests cover admission, invalid tuning, optional
  module absence, cancellation, redeployment, bounded deceleration, terminal
  speed, lateral control, and no powered ascent. JSON and MemoryPack verify
  transactional state normalization. The 192-frame Gravitas-backed paired trace
  proves the controlled impact is more than one-half unit per second less
  downward than free fall, remains within one five-hundred-twelfth of the
  configured terminal speed, preserves fall/landing events, and replays exactly
  in both configurations; the complete instrumented matrices retain exact
  reachable coverage.

### TRB-Issue-103 - Local source graph can build SwiftCollections with two assembly versions

- **Discovered:** 2026-09-03
- **Area:** Cross-stack `UseLocalLsfStack` validation through GridForge
- **Status:** Resolved; verified by the 2026-09-04 clean local-source and
  package-backed matrices.
- **Evidence:** After restoring the affected generated assets to the default
  package cache,
  `dotnet build Gravitas.slnx --configuration Release --no-restore --property:UseLocalLsfStack=true`
  reached compilation and failed with `CS1704` for two `SwiftCollections`
  assemblies. (A preceding `NETSDK1064` was unrelated stale generated metadata
  from an isolated NuGet cache.) GridForge's local stack directly pins
  `SwiftCollections.csproj` to assembly version 7.0.0.0 and also references
  `SwiftCollections.FixedMathSharp.csproj` as version 7.1.0.0. That bridge's
  unqualified nested project reference can propagate the bridge's
  `SemVer`/`AssemblySemVer` into a second SwiftCollections project instance,
  producing 7.1.0.0 beside the intended 7.0.0.0. Diagnostic MSBuild traces also
  showed solution traversal removing `Configuration` from sibling projects not
  present in the solution. A clean test build exposed the related runtime side:
  transitive project discovery discarded the explicit versions and copied a
  rebuilt 0.0.0.0 SwiftCollections assembly beside a GridForge assembly compiled
  against 7.0.0.0.
- **Impact:** A contributor following the solution-wide local-stack workflow
  could receive either a duplicate-assembly compile error or a runtime load
  failure even though every released package family was aligned.
- **Expected:** Local-stack solution builds retain their requested
  configuration; the SwiftCollections FixedMathSharp bridge uses core 7.0.0
  explicitly; and test or benchmark entry points consume direct, versioned
  lower-stack references instead of rebuilding unqualified transitive instances.
  Do not add a binary- copy workaround or compatibility package path.
- **Verification:** Clean `UseLocalLsfStack=true` restore/build/test matrices
  pass in `Release` and `ReleaseLean`: GridForge 819/819 tests in each mode,
  Gravitas 3,930/3,875, and Trailblazer 2,291/2,241. Every build reports zero
  warnings. The resolved Lean output contains one `SwiftCollections.dll` at
  7.0.0.0, `SwiftCollections.FixedMathSharp.dll` at 7.1.0.0, FixedMathSharp at
  7.1.0.0, and GridForge at 9.1.0.0. Normal owner matrices also pass for
  FixedMathSharp (2,708/2,687 tests) and SwiftCollections (1,133/1,105), while
  package-backed GridForge, Gravitas, and Trailblazer repeat the same downstream
  counts with zero build warnings.

### TRB-Issue-102 - Ground contact conflates the support normal with platform orientation

- **Discovered:** 2026-09-03
- **Area:** Trailblazer ground-contact ownership and Gravitas integration
- **Status:** Resolved; verified by the 2026-09-03 focused contract and release
  gates.
- **Evidence:** Gravitas stores `SolidBody.GroundNormal` independently from the
  supporting collider's `FixedTransform`. Trailblazer previously computed
  `GroundCondition.GroundNormal` only as `Platform.Transform.Up`, then used that
  value for slope angle, projection, sliding, and ground-jump direction. On an
  identity-transformed wedge or mesh face, the honest platform transform yielded
  `Up` even when the physical contact normal was sloped. Rotating the snapshot
  to encode the contact normal corrupted moving-platform attachment and carry.
- **Impact:** A host could not preserve both collision geometry and carrier
  motion. Trailblazer could classify a real slope as flat or move an attached
  actor from a fabricated carrier orientation.
- **Expected:** Replace the derived-only `GroundNormal` contract with one
  explicit, serialized `GroundCondition.SurfaceNormal` world-space value that is
  independent of `PlatformSnapshot.Transform`. `Vector3d.Zero` is the sole
  no-sample value; the runtime must not infer `Platform.Transform.Up`. A host
  that wants flat-ground behavior supplies `Vector3d.Up`. Update
  `SetGroundContact`, cloning, motor behavior, XML, and wiki guidance without
  exposing a compatibility alias. Keep Trailblazer core free of Gravitas types.
- **Verification:** `SetGroundContact` now requires the normal, heightmap
  grounding supplies `Up`, and motor fixtures author slope normals explicitly.
  An identity-platform traversal asserts the exact sampled normal, slope angle,
  and projected velocity; a separate contact-refresh regression changes carrier
  transform and normal independently. JSON and MemoryPack preserve the normal,
  Navigator schema 4 rejects schema 3 transactionally, and the explicit `Zero`
  behavior is covered. Full `Release` passes 2,291 tests, full `ReleaseLean`
  passes 2,241, and both exact aggregates cover all 30,074 lines, 11,759
  branches, and 2,951 methods. DocFX and wiki helper gates pass, and two
  independent reviews report no remaining findings.

The older issues below were resolved and verified as part of the 2026-09-01
coverage hardening and code-quality follow-up passes.

### TRB-Issue-101 - Direct-heading allocation gate warms below its measurement window

- **Discovered:** 2026-09-01
- **Area:** Direct navigation-ray steady-state allocation regression
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The full `Release` aggregate reported 4,792 bytes during the
  256-call measurement window after only 16 warmup calls. Three fresh isolated
  processes and the immediate full rerun all reported zero bytes with unchanged
  successful behavior, matching the suite-order-sensitive runtime-tiering
  pattern previously recorded for other allocation gates.
- **Impact:** Tiered compilation can begin during the measured window depending
  on earlier suite execution, intermittently failing a real zero-allocation
  contract without a product allocation regression.
- **Expected:** Warm the exact direct-heading path through one complete
  measurement-sized window before recording current-thread allocations, while
  retaining the strict zero-byte assertion over the following window.
- **Verification:** The gate now uses the same 256-call count for warmup and
  measurement. Focused repeated runs, full `Release`, full `ReleaseLean`, and
  both exact coverage aggregates pass.

### TRB-Issue-100 - Coverage cleanup mixes runtime guards with internal invariants

- **Discovered:** 2026-09-01
- **Area:** Exception guards, pooled-guide ownership, and bounded ingress work
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Coverage hardening added direct exception construction at 24
  ordinary guard sites, retained the same pattern at older guards, expressed one
  bounded scope lookup as `while (true)`, and preserved an A\* double-bind test
  that bypassed the guide cache's detach-before-rent protocol.
- **Impact:** Mixed guard conventions obscured which checks remain required in
  Release, the unconditional loop hid its deterministic scope bound, and the
  internal-abuse test made an impossible pool state look like supported
  behavior.
- **Expected:** Route exact public, lifecycle, serialization, and corruption
  guards through `SwiftThrowHelper`; retain direct throws only where the helper
  cannot preserve the exception contract or terminal control flow; keep proven
  internal ownership as diagnostic assertions; express ingress work with an
  explicit bound; and remove the hollow double-bind test.
- **Verification:** Ninety-three exact guard sites now use `SwiftThrowHelper`,
  the 37 remaining direct throws have documented semantic reasons, scope removal
  performs one allocation-free bounded scan, and supported A\* pooling remains
  covered. Full `Release` passes 2,287 tests, full `ReleaseLean` passes 2,238,
  and both aggregates cover all 30,067 lines, 11,761 branches, and 2,952
  methods.

### TRB-Issue-099 - Transition enumeration assertion checks validity, not ownership

- **Discovered:** 2026-09-01
- **Area:** Immutable transition enumeration diagnostics
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The new transition enumeration assertions accepted any
  structurally valid `NavigationMediumStateRef`, including a reference whose map
  ordinal or cell slot was not owned by the current graph.
- **Impact:** Debug builds could miss the actual ownership violation before the
  direct immutable-directory lookup failed or addressed the wrong map.
- **Expected:** Diagnose exact graph directory and slot ownership before direct
  transition-page enumeration.
- **Verification:** All four assertions now use the graph's exact node-location
  lookup; focused transition tests and the final Release matrix pass.

### TRB-Issue-098 - Empty-graph guard tests manufacture unowned graph references

- **Discovered:** 2026-09-01
- **Area:** Immutable graph ownership and guard-test quality
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The empty-graph fixture passed default node, medium-state, and
  transition references into raw graph-owned enumeration and lookup paths.
- **Impact:** Hollow tests preserved alternate runtime behavior for references
  that production obtains only from the same retained immutable graph.
- **Expected:** Keep durable missing-address and generation behavior, remove
  manufactured unowned references, and express graph-owned inputs as diagnostic
  invariants.
- **Verification:** The focused graph guard suite and both exact coverage
  aggregates pass after removing the invalid rows and redundant fallbacks.

### TRB-Issue-097 - Flow status mapper tests preserve unsupported inputs

- **Discovered:** 2026-09-01
- **Area:** Flow recovery and traversal status mapping
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Mapper theories passed pending, success, edge, blocked, and raw
  undefined statuses even though both production callers handle or exclude those
  states before mapping.
- **Impact:** Unsupported test inputs made impossible mapper defaults look like
  public behavior and obscured the exact terminal-status contract.
- **Expected:** Test only reachable terminal statuses and assert the
  caller-owned input invariant inside each mapper.
- **Verification:** The focused search contract suite and both exact coverage
  aggregates pass with six hollow rows removed.

### TRB-Issue-096 - Failed full-rebuild reconciliation leaves stale readers admitted

- **Discovered:** 2026-09-01
- **Area:** Topology lifecycle safety and operation reconciliation
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A post-snapshot dynamic-slot capacity rejection could occur
  after an unrelated topology removal requested a full seam rebuild but before
  the computed all-close graph published.
- **Impact:** Without materialized work owning an all-closed source, readers
  could acquire an affected-only graph containing stale unaffected topology.
- **Expected:** Mark the graph store safety-pending until an exact physical and
  topology rebuild publishes; an already all-closed materialized owner may
  remain readable.
- **Verification:** The deterministic final-publication predecessor regression
  now rejects the ninth slot, blocks graph acquisition, removes the physical
  change, and proves eventual exact reopening.

### TRB-Issue-095 - Rejected operation reopens before its full seam rebuild

- **Discovered:** 2026-09-01
- **Area:** Operation terminal rollback and automatic seam lifecycle ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Terminal rejection classified any closed scope as immediately
  reopenable even when `_automaticSeamFullRebuildPending` owned that closure.
- **Impact:** Release builds could reopen structural scopes before topology seam
  rebuilding completed, violating the fail-closed lifecycle contract.
- **Expected:** Transfer rejected-operation rollback to the pending full
  rebuild; expose only no graph or an all-closed graph until exact topology
  publication.
- **Verification:** The retained-composition overflow regression proves the
  terminal graph stays fail-closed and later reopens without leaking the
  rejected slot.

### TRB-Issue-094 - Force-override serialization runs MemoryPack in ReleaseLean

- **Discovered:** 2026-09-01
- **Area:** Locomotion-force serialization tests
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `Serialization_ShouldPreservePerInstanceForceOverrides` includes
  its MemoryPack theory row unconditionally, while `ReleaseLean` intentionally
  removes that transport and the shared utility rejects its use.
- **Impact:** The full Lean suite fails before its independent coverage result
  can represent the supported Lean package surface.
- **Expected:** Keep JSON coverage in both configurations and compile the
  MemoryPack row only when the transport is present.
- **Verification:** Run the focused theory and exact aggregate in Release and
  ReleaseLean.

### TRB-Issue-093 - Completed seam lifecycle revalidates an impossible concurrent mutation

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam lifecycle completion
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `MaintainAutomaticSeamLifecycle` calls
  `RevalidateForPublication` immediately after `AdvanceOne` reports complete on
  the same deterministic maintenance boundary. Earlier world changes are
  returned as stale by the worker; only an unsupported concurrent mutation
  between the two calls can make the completion revalidation fail.
- **Impact:** The Release restart arm is unreachable through ordered context
  simulation and would require a probabilistic race test to cover.
- **Expected:** Keep same-boundary revalidation as a diagnostic ownership
  assertion and publish the completed result through the singular lifecycle
  path.
- **Verification:** Preserve stale-before-completion restart tests and run exact
  Release and ReleaseLean coverage.

### TRB-Issue-092 - Runtime baseline scan retains an impossible blocked-owner skip

- **Discovered:** 2026-09-01
- **Area:** Chunked default-baseline capacity lifecycle
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `AdvanceCoveredBaselineRebuilds` skips retained rebuilds marked
  capacity-blocked, but runtime-owned rebuilds are rejected, replaced, or
  removed at the capacity boundary before a later scan can observe that terminal
  flag. The one-below integration fixture retains an incomplete rebuild with a
  blocked count of zero, while direct worker tests already own the local flag
  behavior.
- **Impact:** The runtime condition suggests a terminal owner can persist into a
  later maintenance scan even though lifecycle ownership prevents that state.
- **Expected:** Remove the impossible runtime skip while retaining direct
  bounded- worker capacity behavior.
- **Verification:** Run the focused graph-capacity fixture and include it in
  exact Release and ReleaseLean coverage.

### TRB-Issue-091 - Completed seam work rechecks its proven capacity allowance

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam lifecycle completion
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationAutomaticSeamLifecycleWork.AdvanceOne` receives the
  exact remaining retained-work allowance, yet completion reclassifies the
  result as restart if the same graph-level capacity check fails.
- **Impact:** The second predicate models an impossible divergence between the
  bounded worker and its caller and leaves an unreachable completion branch.
- **Expected:** Keep the invariant as a diagnostic assertion and let publication
  revalidation alone choose complete versus restart.
- **Verification:** Run automatic-seam capacity/lifecycle tests and exact
  Release and ReleaseLean coverage.

### TRB-Issue-090 - Writer-preflight publication fallbacks model unsupported races

- **Discovered:** 2026-09-01
- **Area:** Graph-runtime closure publication and rollback
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** After the maintenance writer has passed `CanPublish`, automatic-
  seam start, composition rollback, and no-change closure reopening retain
  fallback branches that can fail only if an external reader races the same
  single-threaded deterministic maintenance boundary.
- **Impact:** The branches cannot be reproduced through supported ordered
  context simulation, encourage probabilistic race tests, and preserve direct
  classifier tests that do not prove public behavior.
- **Expected:** Express the writer-ownership guarantee diagnostically and keep
  the supported deterministic publication path singular.
- **Verification:** Remove the one-use rollback classifier rows, run writer-
  pressure behavior tests, and include exact Release and ReleaseLean coverage.

### TRB-Issue-089 - Affected-closure regression retains its red-phase assertion

- **Discovered:** 2026-09-01
- **Area:** Structural-composition regression test
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The new materialized catch-up regression proves the affected
  publication was observed and validates both its unaffected and affected cells,
  but its terminal assertion still expects the observation flag to be false from
  the deliberate red phase.
- **Impact:** The focused behavior can pass against stale binaries yet fail in
  the authoritative rebuilt aggregate after the runtime fix is present.
- **Expected:** Require the supported affected-catch-up boundary to be observed
  and remove temporary trace scaffolding.
- **Verification:** Rebuild the focused test, confirm its affected-root
  assertions pass, and include it in exact Release and ReleaseLean coverage.

### TRB-Issue-088 - Volume-anchor coverage injects an unowned node reference

- **Discovered:** 2026-09-01
- **Area:** Volume-anchor evaluation tests and graph ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A direct evaluator test passes `default(NavigationNodeRef)` even
  though production callers resolve the node from the same immutable graph
  before evaluation.
- **Impact:** The test preserves a Release fallback for a state that supported
  endpoint and segment paths cannot supply, mixing invalid-state coverage with a
  useful dependency-capacity assertion.
- **Expected:** Remove the invalid-node row, express graph ownership as a
  diagnostic invariant, and retain the atomic dependency-capacity behavior.
- **Verification:** Run the focused volume-anchor fixture and include it in
  exact Release and ReleaseLean coverage.

### TRB-Issue-087 - Transition refresh test bypasses duplicate-rule validation

- **Discovered:** 2026-09-01
- **Area:** Transition-rule publication tests
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A test composes a raw graph with duplicate global
  transition-rule IDs specifically to bypass the map-fold validation that
  rejects that state in the supported candidate pipeline.
- **Impact:** The invalid-state fixture preserves a Release throw branch that no
  accepted operation can reach and duplicates the real validation test.
- **Expected:** Keep duplicate ownership rejection at the supported fold
  boundary and express refresh-time uniqueness as a diagnostic invariant.
- **Verification:** Run transition publication/fold tests and include them in
  exact Release and ReleaseLean coverage.

### TRB-Issue-086 - Exact coverage omits the ReleaseLean assembly

- **Discovered:** 2026-09-01
- **Area:** CI coverage gates
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The coverage workflow builds and measures only `Release`, while
  `ReleaseLean` compiles a distinct dependency/transport shape.
- **Impact:** Lean-only branches or methods can regress without invalidating the
  claimed exact release gate.
- **Expected:** Collect and enforce an independent exact summary for both
  Release configurations without merging unlike assemblies.
- **Verification:** Exercise the Ubuntu Release/ReleaseLean CI matrix and
  confirm each summary independently reports exact line, branch, covered-method,
  and fully-covered-method totals.

### TRB-Issue-085 - Coverage workflow can validate a different commit after failure

- **Discovered:** 2026-09-01
- **Area:** CI workflow ownership and merge gates
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The post-`build-and-test` workflow checks out the branch tip
  rather than `workflow_run.head_sha`, does not require the triggering run to
  succeed, and runs only after pushes to `main`.
- **Impact:** A newer commit can receive an older run's coverage report, failed
  validation can still launch coverage, and the exact threshold cannot block a
  pull request before merge.
- **Expected:** Bind post-main reporting to the successful triggering SHA and
  enforce exact coverage in the pull-request validation path.
- **Verification:** Validate the workflow expressions and confirm both
  configurations execute exact coverage inside `build-and-test` before merge.

### TRB-Issue-084 - Graph value tests aggregate unrelated contracts

- **Discovered:** 2026-09-01
- **Area:** Internal graph value-semantics tests
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Three large tests in `NavigationGraphValueSemanticsTests`
  combine transition-page behavior, internal key/reference validity, dependency
  identity, and budget accounting in single methods.
- **Impact:** A failure in one contract obscures the exact behavior that
  regressed and can prevent later independent contracts in the same method from
  running.
- **Expected:** Split the aggregate rows into cohesive behavioral groups without
  creating one hollow test per branch.
- **Verification:** Run the focused value-semantics fixture and include the
  resulting tests in exact Release coverage plus the Release/ReleaseLean matrix.

### TRB-Issue-083 - Tests use random GUIDs for stable identities

- **Discovered:** 2026-09-01
- **Area:** Movement-group, grounding, occupancy, and steering tests
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Newly added deterministic behavior tests use `Guid.NewGuid()`
  even though they require only distinct stable identities.
- **Impact:** The randomness does not create a known timing race, but it makes
  exact failure inputs and replays needlessly nondeterministic in a lockstep
  library.
- **Expected:** Use named fixed GUID values that preserve the intended distinct-
  identity relationships.
- **Verification:** Run the affected focused fixtures and include them in the
  Release/ReleaseLean matrix.

### TRB-Issue-082 - Coverage tests force unsupported traversal-medium values

- **Discovered:** 2026-09-01
- **Area:** Map-builder, persistent-collection, and graph-value tests
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Coverage rows cast `99` to `TraversalMedium` to enter internal
  default branches even though supported public construction and query contracts
  reject invalid media before those helpers are called.
- **Impact:** The tests manufacture unreachable state and preserve misleading
  defensive branches instead of documenting supported behavior.
- **Expected:** Remove invalid-enum rows and collapse internal default handling
  to the proven supported-medium invariant where necessary.
- **Verification:** Confirm the invalid casts and unreachable branches are
  absent, then rerun exact Release coverage plus the Release/ReleaseLean matrix.

### TRB-Issue-081 - One-use runtime classifiers exist only for direct coverage tests

- **Discovered:** 2026-09-01
- **Area:** Graph-runtime publication and rollback classifiers
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Several internal static wrappers have one production caller and
  one direct implementation-shaped test in `NavigationGraphCapacityTests`; their
  truth tables duplicate the lifecycle and capacity scenarios that own the real
  behavior.
- **Impact:** The wrappers enlarge the runtime surface and the direct tests can
  remain green while supported publication behavior regresses.
- **Expected:** Inline trivial one-use decisions, remove their direct coverage
  rows, and retain only multi-state classifiers whose precedence is itself a
  meaningful contract.
- **Verification:** Run focused graph-capacity and lifecycle fixtures, then
  rerun exact Release coverage plus the Release/ReleaseLean matrix.

### TRB-Issue-080 - Materialized catch-up drops an owned affected closure

- **Discovered:** 2026-09-01
- **Area:** Structural-composition and materialized-snapshot closure transfer
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `MaintainMaterializedComponentWork` copies only
  `ClosedStructuralComponents` and the all-close flag from a requested
  structural publication. An exact affected closure stores its operation-owned
  component set in the graph's additional closed root, so the copied graph can
  omit that root.
- **Impact:** An affected component can reopen for one published frame while its
  structural composition is still pending, after which the operation records the
  incomplete closure transfer as published.
- **Expected:** Transfer the source graph's complete primary, additional, and
  all-close ownership state onto materialized output.
- **Verification:** Reproduce a bounded affected-closure publication with an
  unrelated physical update, assert that update becomes visible while the exact
  affected endpoint remains closed, and rerun exact Release coverage plus the
  Release/ReleaseLean matrix.

### TRB-Issue-079 - Structural carryover tests construct invalid public inputs

- **Discovered:** 2026-09-01
- **Area:** Structural-composition capacity and physical-capture regression
  tests
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:**
  `PhysicalChangeCarryover_ShouldRetainExactCaptureUntilComponentPublication`
  authors cells through index 7 against a normalized grid ending at index 1, so
  `NavigationMapBuilder.Build` rejects the fixture before publication.
  `ResumedComposition_ShouldRejectDynamicSlotOverflowWithoutLeakingSafetyClosure`
  configures eight overlay cells but only one dynamic slot, so context settings
  validation rejects both theory rows before the claimed capacity lifecycle.
- **Impact:** Three coverage rows are red for malformed setup and provide no
  evidence for physical-capture retention or resumed-composition rollback.
- **Expected:** Keep every authored cell inside normalized bounds and align the
  one-cell overlay limit with the one-slot lifetime ceiling so the fixtures
  reach their intended deterministic runtime boundaries.
- **Verification:** Run the three focused rows, confirm they reach their
  behavioral assertions, and include them in exact Release coverage plus the
  Release/ReleaseLean matrix.

### TRB-Issue-078 - Carryover test stops at a partial fail-closed physical update

- **Discovered:** 2026-09-01
- **Area:** Structural-composition physical-state regression test
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** While a connection-removal composition is retained, the
  unrelated obstacle event can update its retained physical page before the
  broad `GridChanged` resnapshot rematerializes that map. The test stops waiting
  as soon as `ObstacleCount` becomes one, then incorrectly requires
  `IsMaterialized` in that valid intermediate fail-closed state.
- **Impact:** The test reports a physical-state reversion even though it merely
  observed the ordered event prefix before baseline rematerialization, obscuring
  the final behavior it is meant to protect.
- **Expected:** Assert the obstacle page update during carryover without
  conflating it with baseline completion, then wait for rematerialization and
  verify final structural publication retains both values.
- **Verification:** Exercise the bounded fail-closed intermediate state, assert
  the obstacle update during carryover and the fully reconciled map afterward,
  and rerun exact Release coverage plus the Release/ReleaseLean matrix.

### TRB-Issue-077 - Composition capacity rollback loses its closure baseline

- **Discovered:** 2026-09-01
- **Area:** Retained materialization and structural-composition rollback
  ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A retained composition can complete an independently retained
  physical materialization snapshot, then reconcile a newly materialized
  default-backed cell that exceeds the configured dynamic-slot ceiling. The
  materialization completion publishes the composition's requested closure but
  clears the operation-owned pre-closure baseline. The ensuing atomic capacity
  rollback dereferences that missing baseline while reopening structural scope.
- **Impact:** A deterministic GridForge mutation at the final retained-
  composition boundary throws `NullReferenceException` instead of rejecting the
  operation for capacity and restoring the previously published graph.
- **Expected:** Materialization completed on behalf of a structural candidate
  must preserve that candidate's closure baseline until composition publication
  or rollback owns the terminal transition; capacity rejection must remain
  atomic and reopen the exact pre-operation scope.
- **Verification:** Reproduce the two-map dynamic-slot overflow through public
  GridForge mutation at every retained-composition frame boundary, assert at
  least one exact final-publication rejection, verify neither the overlay nor
  the ninth physical slot leaks into the graph, and rerun exact Release coverage
  plus the Release/ReleaseLean matrix.

### TRB-Issue-076 - Delayed physical resnapshot reopens stale composition ownership

- **Discovered:** 2026-09-01
- **Area:** Structural-composition and materialized-snapshot closure ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** After an affected structural closure publishes, a real GridForge
  change can invalidate completed automatic-seam work while bounded physical
  resnapshot work remains retained. The next all-close retry enters
  `ReconcileAndPublish`, which completes the retained materialized work against
  the pre-operation closure baseline and reports `Published` even though the
  requested all-close candidate was not published. The composition then records
  a successful all-close republication and exposes an open graph for one frame
  while stale composition work is still pending. The initial-closure counterpart
  also publishes a retained materialized closure before returning `Deferred`,
  contradicting and redundantly entering the later no-publication fallback.
- **Impact:** Navigation can briefly observe structurally open stale graph state
  during a fail-closed retry, and the deferred-operation path performs duplicate
  reconciliation against the wrong ownership assumption.
- **Expected:** Retained materialized completion must preserve the operation's
  currently requested structural closure; a published retained-materialization
  owner must skip the redundant deferred fallback; stale composition may narrow
  or reopen only after its exact restart/publication lifecycle permits it.
- **Verification:** Reproduce the bounded stale-seam retry through public
  GridForge mutation, assert the requested all-close republication occurs before
  exact restart and revalidation may narrow or reopen the closure, cover the
  initial retained-materialization closure path, and rerun exact Release
  coverage plus the Release/ReleaseLean matrix.

### TRB-Issue-075 - Initial composition duplicates its aggregate capacity rejection

- **Discovered:** 2026-09-01
- **Area:** Initial structural-composition retained capacity
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationStructuralCompositionWork.Advance` receives the exact
  allowance remaining after current-root, operation, baseline, and rebuild
  ownership. When it reports capacity, its retained state already exceeds that
  allowance. The immediately following combined check adds the operation-owned
  closed root and tests the same configured aggregate ceiling, so it necessarily
  rejects every state the earlier boolean rejected.
- **Impact:** Two adjacent rejection paths model one ownership boundary and make
  it appear that initial worker growth and aggregate closed-root capacity can
  disagree.
- **Expected:** Let the combined `work + closed root` check own both
  budget-yield and capacity-yield cases, preserving one atomic
  initial-composition rejection.
- **Verification:** Preserve preflight rejection, measured worker-growth
  rejection, terminal closure reopening, and exact Release coverage.

### TRB-Issue-074 - Terminal composition rollback models an impossible immediate reopen

- **Discovered:** 2026-09-01
- **Area:** Operation-owned structural closure rollback
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `BeginOperationClosureRollback` is called only for
  `ReleaseAndBeginRollback`, which requires a published operation result with
  composition or materialized completion ownership. Every such result either
  published during the current maintenance pass, retains full-rebuild ownership,
  or still has ingress ownership. The fallback that immediately called
  `TryPublishReopenedStructuralScopes` therefore cannot execute through
  supported ordering and contradicts that method's no-publication assertion.
- **Impact:** The duplicate fallback adds an unreachable branch and supports a
  detached truth-table test that fabricates an ownership combination the runtime
  cannot produce.
- **Expected:** Assert the terminal ownership invariant, always defer the reopen
  to the next maintenance pass, and remove the orphaned classifier and static
  matrix.
- **Verification:** Preserve ordinary operation rejection, full-rebuild-owned
  rejection, and next-frame exact closure reopening; then rerun the exact
  Release coverage aggregate.

### TRB-Issue-073 - Internal graph identities expose unsupported boxed equality paths

- **Discovered:** 2026-09-01
- **Area:** Immutable graph identity value contracts
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Twelve internal graph identity structs override `Equals(object)`
  even though every runtime owner compares them through their exact generic type
  and `IEquatable<T>`. The only wrong-runtime-type caller is an unconstrained
  coverage helper. Four of those structs also expose equality operators, forcing
  the boxed override solely to satisfy the compiler even though all operator
  call sites are internal and can use the typed contract.
- **Impact:** Unsupported boxed dispatch adds methods and branches that no graph
  lifecycle can execute, while tests allocate and manufacture unrelated objects
  only to satisfy those branches.
- **Expected:** Remove the redundant boxed overrides and four internal equality
  operators, route their call sites through typed `Equals`, and retain exact
  equality, hashing, ordering, and generic collection-deduplication behavior.
- **Verification:** Build both target frameworks without warnings, keep the
  graph/search/benchmark consumers compiling, preserve typed value-semantics
  tests, and rerun the exact Release coverage aggregate without boxed rows.

### TRB-Issue-072 - Automatic-seam start models impossible post-materialization capacity loss

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam lifecycle retained-work ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A relevant topology event always passes through
  `NavigationMaterializedComponentWork` before automatic-seam start. That gate
  retains the full candidate plus a 3,856-byte owner and succeeds only while the
  current root, candidate, and work fit together. Starting the seam then retains
  the same closed candidate plus only 3,560 bytes. The page envelope is likewise
  strictly smaller than the already-proven current-root and materialized-work
  envelope. Retained materialized completion repeats the same capacity proof
  immediately before its start call.
- **Impact:** The post-publication capacity-requeue disposition and no-effect
  dispatch model a state immutable context limits cannot produce, complicating
  fail-closed ownership and encouraging a coverage-shaped fixture that cannot
  admit its prerequisite graph.
- **Expected:** Assert the dominating materialized-capacity invariant, retain
  unpublished requeue and successful lifecycle start as the only dispositions,
  and remove the unreachable no-effect dispatch and hollow static rows.
- **Verification:** Preserve real unpublished closure requeue, successful
  topology lifecycle start and completion, materialized one-below capacity
  rejection, and the exact Release coverage aggregate.

### TRB-Issue-071 - Address-stamp exhaustion coverage overwrites private generation state

- **Discovered:** 2026-09-01
- **Area:** Address deduplication test design
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `Reset_WhenGenerationIdentityIsExhausted_ShouldFailClosed`
  reaches the maximum generation only by overwriting
  `NavigationAddressStampSet._generation` through `ReflectionUtility`. The
  supported reset lifecycle does not expose that state, so the test is coupled
  to a private field name rather than the no-wrap identity policy used by
  `Reset`.
- **Impact:** A coverage-shaped test can fail after an implementation-only field
  rename or remain green while another generation owner implements different
  overflow behavior.
- **Expected:** Route address-stamp resets through one production-owned bounded
  generation counter and test the exact exhaustion policy directly, without
  mutating a live set's private state.
- **Verification:** Preserve ordinary add, contains, and reset behavior; cover
  successful and exhausted generation advancement; and rerun the exact Release
  coverage aggregate with no reflection-based replacement.

### TRB-Issue-070 - Search coverage tests manufacture private lease lifecycle states

- **Discovered:** 2026-09-01
- **Area:** Search lease and payload-cache test design
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Search contract tests overwrite private guide generations,
  sample ordinals, and flow lease-slot storage through `ReflectionUtility`;
  reach through a public wrapper to bind an already-active private Flow guide;
  manually submit a duplicate guide return after public disposal; and assert
  that unrelated boxed objects do not equal internal payload keys. A selected
  Flow edge always targets an earlier-settled node in an `int`-bounded payload,
  so a lease cannot exhaust its `long` sample ordinal. Exclusive synchronized
  pool ownership likewise prevents a second bind of an active guide.
- **Impact:** These rows add implementation coupling and allocations without
  protecting supported behavior, hide which identity-retirement rules are
  genuinely shared, and make harmless private-layout changes look like runtime
  regressions.
- **Expected:** Keep the public stale-alias behavior, make public alias disposal
  exercise the atomic single-winner detach path, remove impossible ordinal and
  second-bind guards with their reflected fixtures, remove duplicate returns and
  boxed-key paths, and exercise guide/slot generation retirement through
  production-owned pooling and no-wrap policies.
- **Verification:** Preserve ordinary guide reuse, public stale-alias
  fail-close, sample-ordinal overflow policy, disposed/full pool behavior, and
  permanent retirement of exhausted guide and lease-slot identities; then rerun
  the exact Release coverage aggregate without the reflected rows.

### TRB-Issue-069 - NavMotor coverage tests bypass the traversal lifecycle through reflection

- **Discovered:** 2026-09-01
- **Area:** NavMotor lifecycle test design
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A cluster carried forward from the deleted coverage-tail suite
  invokes private NavMotor methods and mutates private force, slope, and state
  fields through `ReflectionUtility`. Those tests can remain green even if the
  supported `TryTraversal` and `FinalizeTraversal` orchestration stops reaching
  the private behavior they claim to protect. Removing them exposed four
  duplicate predicates whose excluded states are already owned by earlier
  supported lifecycle ordering: flight/fall cleanup, later fall clearing,
  post-control non-solid traversal, and steep grounded jump direction.
- **Impact:** Coverage is coupled to implementation names and fabricated states
  instead of public frame behavior, obscuring unreachable logic and producing
  brittle tests that do not protect the host contract.
- **Expected:** Remove private-reflection rows, cover reachable behavior through
  supported traversal frames, and replace the four impossible duplicate arms
  with explicit ownership diagnostics.
- **Verification:** Keep the supported NavMotor lifecycle suite green and rerun
  the exact Release coverage aggregate without reflection-based replacements.

### TRB-Issue-068 - Retained graph work rechecks impossible ownership losses

- **Discovered:** 2026-09-01
- **Area:** Covered-baseline and structural-composition retained ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** An inspectable covered rebuild is neither complete nor
  capacity-blocked and prevents operation advancement while its materialized
  owner retains the exact graph generation, so its map cannot disappear or
  change identity. Structural `Advance` receives an allowance that already
  subtracts the live root, operation work, rebuilds, and any separately retained
  closure baseline. When it returns without `capacityExceeded`, its unchanged
  retained work fits that allowance; successful completion and seam revalidation
  add no retained ownership before the repeated check.
- **Impact:** Rechecking these impossible losses suggests retained work can
  outlive its map owner or grow after its exact allowance check, obscuring the
  actual capacity boundaries and publication races that must stay fail-closed.
- **Expected:** Preserve diagnostic assertions for exact map identity and
  retained-capacity ownership, keep the live `capacityExceeded` rejection, and
  remove the unreachable fallback branches.
- **Verification:** Preserve covered baseline carryover, incomplete and
  completed structural composition, exact one-below capacity rejection, and
  closure publication pressure behavior; then run the full Release coverage
  aggregate.

### TRB-Issue-067 - Explicit ray ordering consolidation leaves an orphaned wrapper

- **Discovered:** 2026-09-01
- **Area:** Navigation ray explicit-chain ordering
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `IsExplicitFirstLegValid` now calls the shared start/end-aware
  comparison directly so its full entry, prior-exit, and ordering policy can be
  exercised deterministically. The former request-bound
  `AreOrderedAlongRay(Vector3d, Vector3d)` wrapper has no remaining callers.
- **Impact:** The private forwarding method is zombie code with no runtime
  behavior and obscures exact method coverage after the policy consolidation.
- **Expected:** Remove the unreferenced wrapper and keep the production-used
  comparison through explicit first-leg and continuation behavior.
- **Verification:** Confirm no wrapper call sites remain, retain forward and
  reverse ray ordering behavior, and restore exact method coverage in the next
  full `Release` aggregate.

### TRB-Issue-066 - Coincident guide append rechecks impossible non-node ownership

- **Discovered:** 2026-09-01
- **Area:** A\* coincident guide-point ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A non-node candidate following a node is preserved by the first
  disposition, and any candidate following a non-node is replaced by the second
  disposition. Reaching the append disposition therefore requires both the prior
  and candidate points to be distinct addressed nodes.
- **Impact:** Rechecking `isNode` inside the append disposition models no
  reachable ownership state and can silently omit the path-node ordinal if the
  surrounding policy regresses.
- **Expected:** Assert node ownership diagnostically and assign the appended
  path-node ordinal directly.
- **Verification:** Keep preserve, replace, distinct-node append, and one-below
  capacity rows, then confirm the redundant branch is absent from the next full
  `Release` coverage aggregate.

### TRB-Issue-065 - Completed seam invalidation rechecks all-close republication ownership

- **Discovered:** 2026-09-01
- **Area:** Resumed structural composition seam revalidation
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A retained composition can return complete only after any
  `RequiresAffectedClosurePublication` state has published. Initial closure
  ownership establishes either that affected closure directly or an all-close
  closure that itself blocks advancement until the affected closure publishes.
  When completed seam revalidation fails, `ResetSeamState` therefore always sees
  `_affectedClosurePublished` and sets `_allCloseRepublishRequired`.
- **Impact:** Rechecking for a missing all-close publication models no retained
  ownership state the structural lifecycle can create and obscures the
  fail-closed response to a stale completed seam cursor.
- **Expected:** Assert the affected-closure ownership invariant and publish the
  all-close rollback directly after failed completed-work revalidation.
- **Verification:** Keep a real retained-work lifecycle regression that
  publishes the affected closure, completes, invalidates the captured seam
  cursor with a topology mutation, and requires all-close republication; then
  run the full Release coverage aggregate.

### TRB-Issue-064 - Pre-begin seam invalidation resets retained work twice

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam queued discovery invalidation
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `RevalidateCompletedCursor` already calls `ResetToSource` when a
  retained validator becomes stale. `AdvanceDiscovery` converted that false
  result into a shared `invalidated` flag and then called
  `RestartDiscoveryAfterInvalidation`, invoking `ResetToSource` a second time.
  The real two-map queued-discovery regression observed `Revision == 2` for one
  stale pre-begin cursor.
- **Impact:** A single world invalidation advances the seam edit ownership token
  and revision twice, making retry identity depend on an implementation detail
  and overstating the number of discarded work generations.
- **Expected:** Return immediately when completed-cursor revalidation has
  already reset the work. Use the shared restart helper only for stale active
  advances and run-stamp mismatches that have not reset yet.
- **Verification:** Keep the queued two-map stale-cursor regression and require
  exactly one revision, zero probes against the next map, and the unchanged
  unpublished seam index; then run the full Release coverage aggregate.

### TRB-Issue-063 - Ray replay rechecks an impossible prior explicit exit miss

- **Discovered:** 2026-09-01
- **Area:** Navigation ray explicit-to-ordinary continuation
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Explicit replay returns `Success` only after the connection exit
  anchor is proven on the requested ray and publication has proven that anchor
  lies inside the destination prism. The stored arrival parameter is the final
  portal crossing into that same convex prism. A following ordinary edge uses
  the later portal crossing out of the prism, so the prior exit anchor must lie
  on the closed segment between those crossings.
- **Impact:** Treating a missing prior exit as ordinary blockage models no
  publishable corridor and obscures the explicit-arrival ownership invariant.
- **Expected:** Retain a diagnostic assertion that the prior exit lies on the
  following ordinary portal leg and remove the impossible Release rejection.
- **Verification:** Keep ordinary-only, explicit-only, and explicit-to-ordinary
  ray behavior, including ordered chained anchors, then confirm the redundant
  branch is absent from the next full `Release` coverage aggregate.

### TRB-Issue-062 - Exact overlay deltas retain an impossible empty baseline capture

- **Discovered:** 2026-09-01
- **Area:** Cell-only overlay physical baseline capture
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Overlay composition preserves the prior valid grid identity when
  `_newAddressCount == 0` and clears it only when at least one new canonical
  address requires a physical delta. Snapshot capture enters its cell-only delta
  path only for a nonmaterialized current instance and a materialized prior
  instance, so every delta reaching that path contains a positive new-address
  count.
- **Impact:** Retaining an empty-delta skip models no instance the composition
  producer can publish and obscures the exact physical-baseline ownership
  contract.
- **Expected:** Assert that a cell-only delta reaching snapshot capture owns at
  least one new canonical address and capture its GridForge baseline directly;
  full zero-address captures remain supported.
- **Verification:** Keep zero-new-address overlay materialization, positive
  exact delta, oversized-delta rebuild, and full empty-map capture behavior,
  then confirm the impossible skip is absent from the next `Release` coverage
  aggregate.

### TRB-Issue-061 - Seam changed-map capture rechecks its assigned ownership phase

- **Discovered:** 2026-09-01
- **Area:** Structural composition automatic-seam changed-map capture
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The resumable seam cursor has only phase 0 and phase 1. A
  phase-0 budget failure returns immediately; every non-returning phase-0 path
  reads one changed map and assigns phase 1 before control reaches the ownership
  block. A resumed dependency-budget failure also retains phase 1.
- **Impact:** Rechecking phase 1 at the ownership block models no reachable
  cursor state and hides broken resumable ownership behind a skipped block.
- **Expected:** Assert the assigned ownership phase diagnostically and execute
  changed-map canonicalization directly, preserving component and dependency
  budget retry behavior.
- **Verification:** Keep one-below changed-map capture and adjacent seam-removal
  peer-map propagation tests, then confirm the redundant phase branch is absent
  from the next full `Release` coverage aggregate.

### TRB-Issue-060 - Ray interval consolidation leaves an orphaned forwarding helper

- **Discovered:** 2026-09-01
- **Area:** Navigation ray interval lookup
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** After the duplicate mapped-target scan was consolidated, the
  private `ContainsParameter(int ordinal, Fixed64 parameter)` overload had no
  callers. All live ray policies call the exact interval-bound overload
  directly.
- **Impact:** The forwarding overload is zombie code with no runtime behavior or
  useful test surface, and retaining it prevents exact method/line coverage from
  describing the executable library accurately.
- **Expected:** Remove the unreferenced overload and retain the production-used
  closed-interval predicate shared by portal and target policies.
- **Verification:** Confirm no call sites remain, keep the before/boundary,
  interior, and after interval matrix, and restore exact line/method coverage in
  the next full Release aggregate.

### TRB-Issue-058 - Deferred operation safety closure rechecks prior publication

- **Discovered:** 2026-09-01
- **Area:** Graph operation terminal maintenance
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** When `ProcessFrame` returns `Deferred` without retained
  composition work, it either stopped before candidate publication or its
  candidate publication failed without publishing. Every deferred structural
  composition path retains `_compositionWork`; any earlier ingress publication
  returns from `Maintain` before operation processing begins. The remaining
  no-composition deferred path therefore cannot have set
  `_publishedThisMaintenance`.
- **Impact:** Rechecking publication ownership models an impossible skip of the
  safety closure and obscures the one-publication-per-maintenance boundary.
- **Expected:** Assert the no-prior-publication ownership invariant and publish
  the exact all-closed safety candidate for deferred work without composition.
- **Verification:** Keep deferred folding, retained composition, publication
  capacity, and operation rollback behavior coverage, then confirm the redundant
  publication check is absent from the next full `Release` coverage aggregate.

### TRB-Issue-057 - Automatic-seam lifecycle allowance counts impossible composition ownership

- **Discovered:** 2026-09-01
- **Area:** Graph automatic-seam retained-work capacity
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A lifecycle is created only by ingress reconciliation requested
  with `startAutomaticSeamLifecycle`. `Maintain` enters that reconciliation only
  when retained operation composition and materialized work are absent. A
  lifecycle deferred through materialized preparation retains the same start
  flag from that exact ingress path. Operation candidate publication never sets
  the flag, so `_lifecycleWork` and `_compositionWork` cannot coexist.
- **Impact:** Subtracting optional composition bytes and pages from lifecycle
  allowance models an unsupported owner combination and makes the capacity
  contract harder to audit.
- **Expected:** Assert lifecycle/composition mutual exclusion diagnostically and
  calculate allowance from the current graph, baselines, and operation owner
  only.
- **Verification:** Keep exact lifecycle byte/page accounting, retained
  materialization, and operation-composition capacity coverage, then confirm the
  nullable composition fallbacks are absent from the next full `Release`
  coverage aggregate.

### TRB-Issue-059 - Final ray admission rechecks an impossible post-segment interval entry

- **Discovered:** 2026-09-01
- **Area:** Navigation-ray final target admission
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** GridForge `GridTraceInterval` values are exact intersections
  with the traced segment and normalize `TEnter` and `TExit` to its closed
  `[0, 1]` parameter range. `TryFinish` reads one of those retained intervals,
  so its `TEnter > Fixed64.One` rejection cannot occur. `TExit < Fixed64.One`
  remains meaningful because it proves that an ordinary final interval does not
  contain the requested destination; `DestinationSuffix` intentionally bypasses
  that requirement.
- **Impact:** Treating a post-segment entry as an ordinary final-target miss
  models no supported GridForge trace result and obscures a broken trace
  normalization invariant behind a silent `false` return.
- **Expected:** Assert the normalized `TEnter <= Fixed64.One` invariant
  diagnostically, while retaining destination containment, suffix, exact finish
  address, and selected-edge predecessor checks as runtime behavior.
- **Verification:** Keep valid-state matrices for ordinary and suffix endpoints,
  `FinishAddress`, and `SelectedEdge`, then confirm the impossible entry arm is
  absent from the next full `Release` coverage aggregate.

### TRB-Issue-056 - Transition-rule seam scans retain terminal-only lookahead

- **Discovered:** 2026-09-01
- **Area:** Incoming and outgoing transition-rule automatic-seam scans
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A rule scan checks the caller's edge slice before advancing its
  automatic-seam cursor. With a positive slice, candidate metering either
  succeeds and consumes the seam immediately or fails with terminal
  `BudgetExceeded`. The shared lookahead flag can therefore remain set only
  after a terminal result that supported callers do not resume.
- **Impact:** Modeling the terminal-only retained seam as resumable state leaves
  duplicate incoming/outgoing branches and suggests a retry contract the
  enumerators cannot honor with their exhausted query meter.
- **Expected:** Rule scans should advance and consume the canonical seam cursor
  directly after the host-slice guard. Base volume-edge merge lookahead remains
  unchanged because that independently ordered merge genuinely retains a seam.
- **Verification:** Keep exact and one-below transition-candidate budgets,
  one-step resumability, and zero-step seam guards in both directions, then
  confirm the terminal-only lookahead branches are absent from the next full
  `Release` coverage aggregate.

### TRB-Issue-055 - Boundary-contact seam discovery rechecks owned cell prisms

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam boundary-contact discovery
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `PrepareDiscoveredPair` is entered with a GridForge boundary
  contact only after both contact configuration keys resolve to map IDs in the
  prepared graph. GridForge emits topology-valid voxel indices for those exact
  normalized configurations, and each resolved map retains that same normalized
  binding. Repeating `TryGetSeamPrism` for either contact endpoint therefore
  cannot fail.
- **Impact:** Treating either repeated prism lookup as ordinary missing seam
  geometry models no supported contact and hides a broken GridForge/graph
  binding invariant behind a silent return.
- **Expected:** Assert both endpoint-prism ownership checks diagnostically,
  while preserving navigation-portal construction failure as a supported
  geometry outcome.
- **Verification:** Keep physical-contact coverage for mapped and unmapped
  navigation addresses plus incompatible portal geometry, then confirm both
  redundant prism-miss branches are absent from the next full `Release` coverage
  aggregate.

### TRB-Issue-054 - Seam constructor allocation gates are sensitive to runtime tiering

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam retained-allocation regression tests
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The seam-refresh constructor gate measured 891,312 allocated
  bytes after a 512-construction warmup, outside its exact 890,880-to-891,135
  window, when run after the broader focused graph suite. Five isolated reruns
  passed. The 432-byte one-time drift is not proportional to the 256 measured
  instances and therefore does not represent a retained per-instance layout
  change.
- **Impact:** Runtime tiering can fail an otherwise exact allocation gate based
  on test order, producing nondeterministic CI failures while the constructor's
  retained-size and persistent-page contracts remain unchanged.
- **Expected:** Execute and discard one complete allocation-sized construction
  window after the broad warmup, then enforce the exact per-instance contract on
  the following window for both seam-refresh and seam-lifecycle work.
- **Verification:** Repeat both focused allocation gates, then run them in the
  full coverage aggregate and the final Release/ReleaseLean matrices before
  moving this item to resolved.

### TRB-Issue-053 - Affected-medium capture rechecks a canonical cursor pair

- **Discovered:** 2026-09-01
- **Area:** Structural composition exact affected-state capture
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `_affectedAddresses` is a canonical set, and the incident cursor
  advances each address ordinal once. Within an address it advances each exact
  medium once, decrementing only before a failed budget debit and adding the
  state only after that debit succeeds. No supported retry can therefore revisit
  a pair already stored in `_affectedMediumStates`.
- **Impact:** Treating a duplicate exact address/medium pair as an ordinary skip
  models no state the resumable cursor can produce and hides broken cursor
  ownership behind fail-soft composition.
- **Expected:** Assert the canonical address/medium cursor ownership
  diagnostically while preserving unchanged-medium filtering and one-below
  dependency-budget retry behavior.
- **Verification:** Keep repeated exact-scope coalescing, multi-medium
  expansion, and one-entry dependency-budget regressions, then confirm the
  duplicate pair branch is absent from the next full `Release` coverage
  aggregate.

### TRB-Issue-052 - Source-row seam filtering tolerates an impossible absent source record

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam row reconstruction
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `ShouldFilterSourcePair` is called only for a pair emitted by
  the source automatic-seam row. A matching pair delta is created from the same
  source index's exact `TryGetPairRecord` result before row reconstruction. The
  source row and pair-record index are immutable parts of one
  `NavigationAutomaticSeamIndex`, so a matching delta reached through that row
  cannot have a null `SourceRecord`.
- **Impact:** Treating the source record as optional invents geometry and active
  defaults that no valid seam index can own, and can hide a broken internal
  index invariant behind ordinary row filtering.
- **Expected:** Assert source-record ownership diagnostically and filter
  dependency rows by geometry changes while filtering active rows by geometry or
  activity changes.
- **Verification:** Keep the mixed mapped/unmapped seam behavior coverage and
  cover the complete dependency-versus-active row filtering policy, then confirm
  the nullable source-record fallbacks are absent from the next full `Release`
  coverage aggregate.

### TRB-Issue-051 - Graph node readers revalidate immutable traversal ownership

- **Discovered:** 2026-09-01
- **Area:** Graph node-state and structural native-edge enumeration
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Every exact-medium `TryGetNodeState` caller retains a medium
  that was validated when its query, transition, or evaluator was created; the
  convenience overload supplies `Solid` directly. Structural native-edge
  enumeration is likewise entered only by the graph-created surface enumerator
  retaining its exact source `NavigationNodeRef`. An immutable graph can later
  suppress that node's effective cell, but cannot lose the source's map/slot
  location.
- **Impact:** Treating an unknown medium or missing structural source location
  as ordinary empty traversal results models states no production owner can
  create and hides broken internal ownership behind fail-soft behavior.
- **Expected:** Assert exact-medium and structural-source ownership
  diagnostically, while preserving closed-medium rejection, raw-state lookup,
  present-state filtering, and suppressed effective-cell behavior as supported
  graph outcomes.
- **Verification:** Keep the real per-medium closure, closed candidate, native
  traversal, and suppressed structural-enumerator behavior tests, then confirm
  the duplicate ownership branches are absent from the next full `Release`
  coverage aggregate.

### TRB-Issue-050 - Covered-address rebase rechecks an impossible graph-prism miss

- **Discovered:** 2026-09-01
- **Area:** Flow local-rebase candidate admission
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Flow supplies GridForge's covered-address cursor only with exact
  generations obtained from the retained navigation graph. Every emitted
  candidate therefore carries a configuration key owned by that graph and a
  topology-valid voxel index. Resolving the key back to its map and then asking
  the same normalized binding for that index's prism cannot fail.
- **Impact:** Treating the repeated prism lookup as ordinary candidate rejection
  models no supported production input and hides a broken graph/GridForge
  ownership contract behind fail-soft local recovery.
- **Expected:** Assert the retained configuration-prism ownership
  diagnostically, while preserving exact-prism containment, mapped-node,
  closed/present state, payload lookup, checked distance, and work-budget
  rejection as live candidate-admission behavior.
- **Verification:** Keep the real closed-component candidate regression, the
  exact hex-prism broad-phase rejection, payload lookup statuses, checked
  distance overflow, and one-below sample budgets, then confirm the redundant
  prism-miss branch is absent from the next full `Release` coverage aggregate.

### TRB-Issue-049 - Initial composition capacity rejection retains a stale closure baseline

- **Discovered:** 2026-09-01
- **Area:** Graph structural-composition capacity rejection
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Initial structural composition captures
  `_ownedStructuralClosureBaseline` before checking the exact combined retained
  work against the newly closed graph. When that check returns
  `PermanentCapacity`, no composition work or closed graph was published, so
  terminal rejection takes the ordinary release path and nothing clears the
  independently retained baseline.
- **Impact:** A capacity-rejected operation can leave obsolete structural
  closure ownership in the runtime. Later operations may preserve or account
  that stale baseline even though the operation that captured it never owned a
  published closure.
- **Expected:** Build the prospective closure against the current baseline,
  verify its exact combined capacity, and only then capture runtime ownership.
  All initial-composition capacity exits return `PermanentCapacity` without
  disturbing an older closure owner; retained-composition rollback remains
  unchanged after a closure has actually been published.
- **Verification:** Keep the initial scratch-capacity rejection regression and
  exact retained-capacity boundaries, confirm diagnostics retain no abandoned
  work, then run the full Release and ReleaseLean gates before moving this item
  to resolved.

### TRB-Issue-048 - Default baseline discovery rechecks impossible stale output

- **Discovered:** 2026-09-01
- **Area:** Default-backed baseline covered-address discovery
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `GridWorld.AdvanceCoveredAddresses` initializes `outputCount` to
  zero and returns `Stale` before generation binding or address enumeration when
  the cursor snapshot is no longer current. Its public contract also requires a
  stale result to write no output. Therefore a positive `outputCount` already
  proves the `invalidated` flag derived from `status == Stale` is false.
- **Impact:** Rechecking `!invalidated` after `outputCount > 0` models a
  producer result GridForge cannot return and leaves an unreachable branch in
  default baseline discovery.
- **Expected:** Assert the stale-with-zero-output producer contract
  diagnostically and let positive output enter baseline capture directly, while
  preserving stale reset, recapture identity validation, and exact probe
  accounting.
- **Verification:** Preserve stale cursor and successful default discovery
  regressions, then confirm the redundant predicate is absent from the next full
  `Release` coverage aggregate and run the full Release and ReleaseLean gates.

### TRB-Issue-047 - Default-backed suppression fails to reserve a dynamic semantic address

- **Discovered:** 2026-09-01
- **Area:** Overlay folding and default-cell dynamic slot ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationOverlayFoldWork` reserved an unbaked address in
  `DynamicAddresses` only for `Set`. A direct `Suppress` over inherited default
  semantics therefore published a tombstone without assigning the stable dynamic
  slot needed to materialize it. Existing compose-only tests manually supplied
  the expected address registry and did not exercise the production fold.
- **Impact:** A default-cell tombstone can be omitted from the published graph,
  so a cell intended to be blocked may continue inheriting passable default
  semantics.
- **Expected:** Unbaked `Set` and default-backed `Suppress` operations both
  reserve a stable dynamic address. Once assigned, that identity remains
  reserved across suppression and reversion so later map replacement cannot
  absorb or reuse its slot.
- **Verification:** Keep the production fold regression covering direct
  default-backed suppression plus permanent reservation across reversion and
  explicit-only Set-to-Suppress changes, then run focused map/graph tests and
  the full Release and ReleaseLean matrices before moving this item to resolved.

### TRB-Issue-046 - Internal area-policy lookups retained impossible null branches

- **Discovered:** 2026-09-01
- **Area:** Pathing graph dependency validation and guide/ray admission
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationAreaCatalog.TryGet` returns `true` only after
  assigning an element from its immutable non-null policy array, yet its
  nullable `out` contract forced callers such as `TrailblazerGuideService` and
  `NavigationRayWork` to recheck the successful result for `null`.
  `NavigationWorldGraph.IsDependencyCurrent` likewise guarded a non-nullable
  internal `GraphDependencyStamp` parameter against `null`; every typed caller
  supplies a real stamp.
- **Impact:** These defensive branches describe states the internal contracts
  cannot produce, obscure the actual stale-policy decision, and create zombie
  paths that cannot be covered through supported behavior.
- **Expected:** Successful catalog lookup should expose a non-null policy, and
  dependency validation should rely on its non-nullable stamp contract while
  continuing to reject missing or content-mismatched policies.
- **Verification:** Keep missing-policy and mismatched-policy behavior coverage,
  run focused guide/ray/dependency tests, then complete the full Release and
  ReleaseLean matrices before moving this item to resolved.

### TRB-Issue-001 - Large-map delta baseline capture can drop new addresses

- **Discovered:** 2026-08-31
- **Area:** Pathing graph maintenance and dynamic-address baseline capture
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** With 65 baked addresses, one newly added dynamic address, and a
  64-address baseline scratch capacity,
  `NavigationMapInstance.CopyNewCanonicalAddresses` returns zero because the
  map's total address count exceeds the scratch span.
  `NavigationWorldGraph.CaptureMaintenanceSnapshot` then requests a delta
  capture with `AddressCount == 0` and removes the stale rebuild instead of
  capturing the one new address.
- **Impact:** A newly added physical address can miss its required baseline
  refresh whenever the map is larger than the per-frame baseline scratch
  capacity, leaving navigation state stale for that address.
- **Expected:** Delta capture should be bounded by the number of new canonical
  addresses, not the map's total address count, and should retain resumable work
  when the new-address delta exceeds available scratch capacity.
- **Verification:** Keep the controlled 65-baked-plus-one-dynamic regression,
  then run focused graph lifecycle tests in `Release` and `ReleaseLean` before
  moving this item to resolved.

### TRB-Issue-002 - Operation rollback can reopen stale physical state

- **Discovered:** 2026-08-31
- **Area:** Pathing graph operation-closure rollback and grid-change ingress
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** With an eight-cell dense map and a 98-page graph cap, a retained
  eight-suppression overlay closes the affected structural scope. Adding
  obstacles to all eight GridForge cells before the next frame makes the overlay
  publication fail with `CapacityExceeded` and enter
  `BeginOperationClosureRollback`. The immediately published rollback graph has
  an open structural scope while its first cell still reports
  `IsMaterialized == true`, `ObstacleCount == 0`, and `IsBlocked == false`, even
  though the committed world contains the obstacles and a physical resnapshot
  has been requeued.
- **Impact:** Navigation can observe stale passable physical state for at least
  one frame after a capacity-rejected operation instead of remaining fail-closed
  until committed GridForge changes are reconciled.
- **Expected:** Operation rollback must not reopen a structural scope from a
  stale snapshot while safety or resnapshot work is pending. The graph should
  remain fail-closed until the requeued physical state has been reconciled and
  safely published.
- **Verification:** Keep the exact-boundary rollback regression, prove the
  stale-open state before the fix, then run focused graph lifecycle tests in
  `Release` and `ReleaseLean` before moving this item to resolved.

### TRB-Issue-003 - Off-axis explicit progress can steer back to a completed entry

- **Discovered:** 2026-08-31
- **Area:** Flow-guide selected-edge progress for explicit connections
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** On a valid zero-witness explicit connection whose entry and exit
  anchors are offset from the cell centers, sampling at the entry first points
  toward the resolved portal as expected. Sampling the same guide at that
  resolved portal then returns the normalized portal-to-entry direction instead
  of the portal-to-exit direction. The source-cell branch re-evaluates
  `sourceFoot -> entry` with the portal position, classifies the completed
  approach as not passed, and sends the agent backward.
- **Impact:** A navigator following an off-axis explicit connection can
  oscillate or backtrack after reaching the portal rather than continuing
  through the authored connection corridor.
- **Expected:** Once the actual foot is on the certified directed
  `entry -> portal -> exit` corridor, guide sampling should select the current
  or next directed leg and must not steer back to the completed entry approach.
- **Verification:** Keep the public guide regression at the entry and resolved
  portal, prove the pre-fix backward heading, then verify forward portal-to-exit
  guidance in `Release` and `ReleaseLean`.

### TRB-Issue-004 - Validation workflow names the wrong library

- **Discovered:** 2026-08-31
- **Area:** Contributor issue-tracker guidance
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The validation workflow in this Trailblazer tracker instructs
  contributors to run "Gravitas" release gates.
- **Impact:** Contributors can reasonably interpret the release checklist as
  belonging to another repository and miss Trailblazer's actual validation
  requirements.
- **Expected:** The workflow should name Trailblazer and its real release gates.
- **Verification:** Correct the library name and keep the workflow aligned with
  Trailblazer's documented `Release`, `ReleaseLean`, coverage, allocation, and
  benchmark validation.

### TRB-Issue-005 - Failed jump registration partially mutates public state

- **Discovered:** 2026-08-31
- **Area:** Navigation motor jump lifecycle and context validation
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Calling `RegisterJump()` on a standalone `JumpLocomotion`
  increments `JumpCount` and sets the jumping/hold flags before reading
  context-owned simulation time. Missing-context validation then throws, but the
  earlier state mutations remain visible.
- **Impact:** A failed public jump command is not transactional. Reusing or
  subsequently binding the object can inherit a jump that never successfully
  started, producing state divergence from the command result.
- **Expected:** Required context state must be validated and captured before
  mutating jump lifecycle fields; a failed registration must leave the object
  unchanged.
- **Verification:** Keep a standalone regression that asserts the exception and
  exact pre-call state, then run the complete jump and controller lifecycle
  suites in `Release` and `ReleaseLean`.

### TRB-Issue-006 - A\* snapshot pressure discards active steering intent

- **Discovered:** 2026-08-31
- **Area:** Navigation steering and A\* guide lease refresh
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** With an active public A\* steering query and every graph
  snapshot lease held, the current guide reports `CapacityExceeded`. The next
  heading is correctly zero while pressure remains, but
  `ValidateGraphMovementPath` handles only `Stale` as retryable and returns
  false. `GetHeading` then calls `Arrive()` and clears `CurrentQuery`; the
  focused regression expected the exact original query and observed `null`. Flow
  sampling already treats the equivalent `CapacityExceeded` status as retryable.
- **Impact:** Temporary graph-snapshot capacity pressure permanently cancels an
  A\* movement request instead of pausing and retrying it, so otherwise
  deterministic navigation intent depends on transient lease availability.
- **Expected:** An active A* lease that becomes capacity-blocked must preserve
  the exact query and schedule a retry, matching initial A* admission and Flow
  sampling behavior. No heading should be emitted while pressure remains.
- **Verification:** Keep the public graph-lease-pressure regression red before
  the fix, then prove zero heading plus exact query preservation and successful
  retry after releasing the held snapshots in `Release` and `ReleaseLean`.

### TRB-Issue-007 - Cell-only overlay capture reads past its frame-change batch

- **Discovered:** 2026-08-31
- **Area:** Pathing graph structural-composition capture
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A valid one-cell map followed by a cell-only suppress overlay
  advances through the overlay's sole exact-address scope under a one-entry
  dependency budget. On the next resumable capture step,
  `NavigationStructuralCompositionWork.TakeNextRawScope` exhausts the last map,
  increments `_changeIndex` to `changeCount`, then continues its private loop
  and reads `_changes[_changeIndex]`. The focused regression deterministically
  throws `IndexOutOfRangeException` from `TakeNextRawScope`, called by
  `AdvanceChangedMapCapture` and `Advance`.
- **Impact:** Structural publication for an otherwise valid cell-only overlay
  can crash graph maintenance instead of retaining and atomically publishing the
  exact affected address. The failure occurs after bounded work resumes, so low
  maintenance budgets make it directly reachable in normal operation.
- **Expected:** Exhausting the final overlay map must finish the raw-scope
  cursor and return to `HasNextRawMapId` without indexing past the frame-change
  batch. The exact component and address debits must remain resumable and the
  overlay must publish without stale membership.
- **Verification:** Keep the one-cell suppress regression with one dependency
  entry per frame red before the fix, then prove exact component/address retry,
  successful completion, and suppressed effective state in focused `Release` and
  `ReleaseLean` graph tests.

### TRB-Issue-008 - Partial Navigator load dereferences absent steering

- **Discovered:** 2026-08-31
- **Area:** Navigator populate-existing serialization and committed-cell rebuild
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A supported derived partial-controller shell with no steering
  component passes the guarded nullable component-load path for both JSON and
  MemoryPack. `Navigator.RecordData` then calls
  `RebuildCommittedCellState(emitChange: false)`, which dereferences
  `_steering!` while resolving the committed area policy. The focused
  two-transport regression deterministically throws `NullReferenceException`
  after otherwise successful component population.
- **Impact:** Hosts using a partial Navigator shell cannot transactionally
  restore an otherwise valid record, even though the explicit load contract
  accepts absent optional controller components.
- **Expected:** Post-load committed-cell rebuilding must preserve an absent
  steering component and use a null area policy for the unguided partial shell.
  Complete Navigators must retain their exact policy and pending-policy consume
  behavior.
- **Verification:** Keep the JSON and MemoryPack partial-shell regressions red
  before the fix, then prove successful load, null policy, and unchanged full
  Navigator committed-cell behavior in focused `Release` and `ReleaseLean`
  serialization suites.

### TRB-Issue-009 - Stale copied A\* reservation can release its replacement

- **Discovered:** 2026-08-31
- **Area:** A\* payload cache reservation ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationAStarPayloadReservation` contains only maximum bytes
  and a lease-slot flag. Reserving A, copying it, releasing A, reserving B for
  the same byte count, and then releasing the stale copy of A passes cache
  validation and consumes B's accounting. Releasing the legitimate B reservation
  then throws `InvalidOperationException` from `NavigationAStarPayloadCache`
  because the counters are inconsistent. The focused regression is deterministic
  and uses only normal reservation APIs; Flow reservations already carry owner,
  slot, and generation identity.
- **Impact:** A stale value copy can corrupt cache byte and lease-slot
  ownership, causing valid A\* publication/release operations to fail and
  potentially making capacity availability depend on alias order.
- **Expected:** Each A\* reservation must have exact cache-owned identity so a
  stale alias cannot release, publish, or otherwise mutate a replacement slot.
  Stale operations must fail closed without changing the live reservation's
  accounting.
- **Verification:** Keep the copied-alias regression red before the fix, then
  prove the stale release is rejected, B remains usable and releasable, and all
  bytes/lease slots return to zero in focused `Release` and `ReleaseLean` cache
  tests.

### TRB-Issue-010 - Null legend tokens alias the empty built-in token

- **Discovered:** 2026-08-31
- **Area:** Token-map authoring and public argument validation
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationTokenLegend.Register` and `TryGetEntry` normalize a
  runtime `null` token through `token?.Trim() ?? string.Empty`. The built-in
  legend deliberately registers the empty token as `SkipCell`, so a null lookup
  succeeds as though the host supplied an authored empty token; a custom legend
  can likewise register null as its empty key.
- **Impact:** The non-null public token contract silently aliases invalid host
  input to valid authoring. This can hide a missing token value and make null
  handling depend on whether the empty token happens to be registered.
- **Expected:** Registration and lookup reject null deterministically while
  preserving ordinal trimming and the intentional empty/whitespace token.
- **Verification:** Keep public registration and lookup regressions that reject
  null and prove whitespace still resolves the explicit empty built-in token in
  `Release` and `ReleaseLean`.

### TRB-Issue-011 - Combined volume-union work reports the wrong limit

- **Discovered:** 2026-08-31
- **Area:** Volume traversal guide-sample work accounting
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A legal four-cell gas union succeeds when the guide bridge has
  the exact combined lookup-and-covered-address allowance. Reducing that
  combined allowance by one makes GridForge report `CandidateWorkLimitExceeded`,
  but `NavigationVolumeAnchorEvaluator` maps the result to `CapacityExceeded`
  instead of `BudgetExceeded`.
- **Impact:** A Flow guide can report workspace exhaustion when only its public
  per-sample work budget ran out, preventing callers from distinguishing a
  retryable budget boundary from a fixed capacity limit.
- **Expected:** Candidate-work exhaustion derived from
  `NavigationWorkMeter.RemainingGridCandidateWork` must report `BudgetExceeded`;
  workspace grid, address, and output ceilings must continue to report
  `CapacityExceeded` at their exact boundaries.
- **Verification:** Keep the exact/one-below combined guide-work regression and
  the independent workspace-capacity boundary regression, then run focused
  volume and guide tests in `Release` and `ReleaseLean`.

### TRB-Issue-012 - Explicit ray maps profile impassability to stale

- **Discovered:** 2026-08-31
- **Area:** Ordered navigation-ray explicit-edge status mapping
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A valid explicit corridor authored for radius `1/8` is sampled
  by an agent with radius `1/4`. The agent fits the cells and their actual
  radius-`1/2` portal, but exceeds the connection's intentional clearance.
  `TraversalEvaluator.BeginExplicitEdge` returns `Impassable` and
  `NavigationRayWork.MapExplicitStatus` converts it to `Stale`; the selected ray
  therefore reports `Stale` instead of `Blocked`.
- **Impact:** A structurally current guide can be invalidated as stale merely
  because its agent is too large for one authored connection, obscuring a normal
  geometric blockage and triggering unnecessary cache or query refresh.
- **Expected:** Explicit-edge profile impassability must report `Blocked` while
  structural certificate failures remain `Stale` and checked arithmetic remains
  `CostOverflow`.
- **Verification:** Keep the legal clearance matrix covering passable,
  off-corridor blocked, and too-wide blocked outcomes, then run focused ray and
  guide tests in `Release` and `ReleaseLean`.

### TRB-Issue-013 - Synchronous A\* rejection publishes through an absent search

- **Discovered:** 2026-08-31
- **Area:** A\* query admission and publication lifecycle
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A transition-disabled query whose start and target media differ
  is rejected synchronously as `NoPath` by `NavigationQueryAdmissionWork.Begin`.
  `NavigationAStarQueryWork` marks that terminal status ready without creating a
  surface search, but `Publish` dereferences `_search!.Result`; the existing
  transition-shape class regression deterministically throws
  `NullReferenceException`.
- **Impact:** A valid fail-closed query rejection can crash guide acquisition
  instead of returning its exact terminal status.
- **Expected:** Publication must preserve synchronous admission statuses when no
  search was created and must never dereference absent search state.
- **Verification:** Keep the transition-shape regression red before the fix,
  then prove the complete class and focused A\* orchestration slice in
  `Release`.

### TRB-Issue-014 - A\* pending action leaks an undefined public status

- **Discovered:** 2026-08-31
- **Area:** A\* guide status projection
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationAStarGuideLease.TryAdvanceWaypoint` legitimately
  returns internal `Pending` when the current step is a semantic action. The
  arithmetic public mapper subtracts one from that zero-valued status and emits
  byte value 255 instead of the documented fail-closed `Stale`. Existing ladder
  and explicit-transition regressions observe the undefined enum value. The
  sibling Flow request mapper has the same arithmetic default hazard. Removing
  A\* guide generation retirement also permits an eventually wrapped generation
  to make a stale copied lease alias a later acquisition.
- **Impact:** Public guide callers can receive a status outside the declared
  contract when they attempt to cross an action as an ordinary movement step.
- **Expected:** Every internal status, including `Pending`, must project to a
  declared public status; an ordinary advance at an action remains `Stale`.
  Exhausted guide identities must retire instead of wrapping into an earlier
  copied lease generation.
- **Verification:** Keep both public transition regressions red before the fix,
  then prove exact `Stale` results in the focused guide and simulation slice.

### TRB-Issue-015 - Flow completion ordinal wrap aliases an earlier action

- **Discovered:** 2026-08-31
- **Area:** Flow guide transition occurrence ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Allowing `_sampleOrdinal` to increment unchecked from
  `long.MaxValue` permits a live guide to wrap its completion stamp and sample a
  transition as a new successful occurrence. The existing selected-transition
  regression expects the guide to retire as `Stale` before that alias and
  instead observes `Success`. Unchecked Flow guide-generation reuse has the same
  eventual stale-copy alias risk at the lease-owner boundary.
- **Impact:** A sufficiently long-lived or restored near-limit guide can reuse a
  completion identity, weakening exact-once semantic action ownership.
- **Expected:** Flow guides must fail closed before source, completion, or lease
  generation progression would wrap an exact occurrence identity.
- **Verification:** Preserve the ordinal-boundary regression, restore exact
  stale retirement, and run the focused Flow guide lifecycle slice in `Release`.

### TRB-Issue-016 - Retired Flow guide shell leaves a reusable slot without a shell

- **Discovered:** 2026-08-31
- **Area:** Flow guide generation retirement and bounded shell pooling
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Retiring the sole preallocated guide at `ulong.MaxValue`
  correctly keeps that shell out of the reuse pool while recycling its payload
  lease slot. The next valid guide acquisition then calls `RentGuideUnderLock`
  with `_freeGuideCount == 0`; the method decrements to `-1` and throws
  `IndexOutOfRangeException` instead of restoring the configured guide capacity.
- **Impact:** The fail-closed generation-retirement safeguard can permanently
  break later Flow guide acquisition at an otherwise available lease slot.
- **Expected:** Normal guide returns remain allocation-free. A rare permanently
  retired shell may allocate exactly one replacement so configured guide
  capacity remains available without reusing the exhausted identity.
- **Verification:** Keep the max-generation regression red before the fix, then
  prove the retired shell stays stale, the replacement is a distinct object,
  active accounting returns to zero, and the focused cache/guide slice passes in
  `Release`.

### TRB-Issue-017 - Flow payload lease-slot generation can wrap onto a stale identity

- **Discovered:** 2026-08-31
- **Area:** Flow payload reservation and lease ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationFlowFieldPayloadCache.TryIssueLeaseSlot` increments a
  recycled slot generation with unchecked `ulong` arithmetic, while
  `RecycleLeaseSlot` always returns a max-generation slot to the free pool. A
  slot released at `ulong.MaxValue` is therefore reissued at generation zero,
  allowing an exact copied reservation or lease identity to alias a later
  occupant after the counter eventually cycles.
- **Impact:** A stale Flow reservation or lease copy can eventually be accepted
  as the owner of unrelated live payload state, weakening exact disposal and
  immutable payload ownership.
- **Expected:** A slot whose generation reaches `ulong.MaxValue` is permanently
  retired and reduces configured Flow lease capacity rather than wrapping.
  Normal issue/recycle paths remain allocation-free.
- **Verification:** Keep the max-generation reservation regression red before
  the fix, prove the exhausted slot cannot be reissued or released by a stale
  copy, and run the focused Flow cache/guide slice in `Release`.

### TRB-Issue-018 - Flow traversal projection can misclassify cost overflow

- **Discovered:** 2026-08-31
- **Area:** Flow search terminal-status projection
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The full `Release` coverage aggregate reproduced
  `PublicGuideRequest_ShouldReportCostOverflowWithoutLeakingALease` for the Flow
  algorithm with public status `CapacityExceeded` instead of `CostOverflow`.
  `NavigationFlowFieldWork.MapTraversalStatus` projected the internal traversal
  enum by subtracting one even though the Flow enum orders `CostOverflow` before
  `CapacityExceeded`.
- **Impact:** Callers cannot distinguish fixed-point path-cost overflow from
  workspace capacity exhaustion and may respond with the wrong recovery path.
- **Expected:** Flow search maps every internal terminal traversal status to its
  exact public guide meaning without relying on enum ordinal alignment.
- **Verification:** Keep the public A\*/Flow guide regression, restore an
  explicit exhaustive projection, and prove the focused guide slice plus the
  fresh `Release` coverage aggregate pass.

### TRB-Issue-019 - Dormant automatic seams can be published as traversable

- **Discovered:** 2026-08-31
- **Area:** Automatic seam refresh and sparse physical-presence lifecycle
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The full `Release` coverage aggregate reproduced four seam
  lifecycle regressions after a publication-invariant simplification. A durable
  seam pair intentionally remained in the structural graph after its sparse
  endpoint disappeared, but `FillSeam` treated active-row membership as
  sufficient and emitted a live cross-map edge instead of rechecking the
  endpoint's runtime presence.
- **Impact:** Navigation can expose a cross-map traversal through a physically
  absent sparse cell during removal, stale-probe restart, or respawn schedules.
- **Expected:** Durable dormant seam geometry remains available for structural
  composition, while a live traversal edge is published only when both exact
  runtime endpoints are present.
- **Verification:** Preserve the sparse removal, completed-probe invalidation,
  affected-closure, and respawn regressions; restore the fail-closed runtime
  readiness check; then prove the focused seam slice and fresh `Release`
  coverage aggregate pass.

### TRB-Issue-020 - Explicit corridor shortcuts can clip blocked geometry

- **Discovered:** 2026-08-31
- **Area:** Explicit surface-edge routing and A\* guide construction
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The full `Release` aggregate reproduced two existing explicit
  route regressions after corridor checks were reduced to publication
  assertions. A target-foot exit leg that crossed the opposite wall returned a
  successful path instead of `NoPath`, and a positive-radius corner-clipping
  entry leg selected the cheaper explicit edge instead of the valid native
  alternative.
- **Impact:** An agent-specific explicit approach or exit segment can pass
  through blocked geometry even though the authored connection's immutable
  corridor was valid when published.
- **Expected:** Publication validates the authored connection, while runtime
  routing still validates the exact profile-sized entry, witness, and exit
  segments selected for the current query.
- **Verification:** Keep both existing clipping regressions, restore the
  runtime-significant corridor checks, and prove the focused surface A\* slice
  plus a fresh `Release` coverage aggregate pass.

### TRB-Issue-021 - Structural resume contains an impossible safety-closure branch

- **Discovered:** 2026-09-01
- **Area:** Graph composition resume state machine
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Exact zero-hit caller tracing initially suggested that a middle
  resume branch could publish an all-close safety root as an operation result.
  Deeper state proof showed the branch cannot be entered:
  `_allCloseRepublishRequired` is set by seam reset only after affected-closure
  publication, which itself requires seam capture to be complete. A resumed
  `Advance` therefore skips the seam loop; a requirement already present is
  handled by the entry guard, while inter-step staleness is handled by the
  post-advance revalidation path.
- **Impact:** The unreachable branch duplicates live safety-publication logic,
  obscures which path owns `Deferred` translation, and creates a misleading
  maintenance and coverage obligation.
- **Expected:** Remove the impossible middle branch while retaining the entry
  guard and post-advance stale revalidation paths that can actually publish an
  all-close safety root.
- **Verification:** Document the state invariant at the removal point, keep the
  live affected/all-close lifecycle regressions, and run the focused structural
  composition slice plus a fresh `Release` coverage aggregate.

### TRB-Issue-022 - Query preparation duplicates world-epoch rejection

- **Discovered:** 2026-09-01
- **Area:** A\* and Flow admission-to-search handoff
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Exact branch tracing found identical post-admission epoch checks
  in both query workers. Admission already rejects an epoch change before it
  binds a resolved query. If the world changes in the following concurrency
  window, cache checkout validates the payload epoch and a cache miss passes the
  original epoch into an A\* or Flow worker that rejects it before doing search
  work.
- **Impact:** The duplicate early exits add two state-machine paths and a
  misleading coverage obligation without changing the public fail-closed `Stale`
  outcome.
- **Expected:** Keep epoch ownership in admission, cache validation, and the
  search workers; remove the duplicate handoff checks.
- **Verification:** Preserve the admission, cache, A\*, and Flow world-mutation
  regressions, then run the focused query slices and a fresh `Release` coverage
  aggregate.

### TRB-Issue-023 - A\* publication exposes an impossible capacity fallback

- **Discovered:** 2026-09-01
- **Area:** A\* query publication and payload reservation ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Caller tracing showed that a gate-owned reservation has exact
  cache ownership, remains current for the query lifetime, and is sized to the
  worker's maximum payload before search starts. After a completed search,
  `TryPublish` therefore cannot reject a current payload for capacity. The old
  fallback could be reached only by constructing query work around an undersized
  reservation outside the admission gate's invariants.
- **Impact:** The impossible `CapacityExceeded`/`NoPath` split suggests a
  recoverable runtime outcome that the real state machine cannot produce and
  obscures that publication failure at this boundary means stale proof.
- **Expected:** Assert the reservation invariant in diagnostic builds and map
  the only live publication failure to `Stale`.
- **Verification:** Keep exact cache reservation-capacity tests at the cache
  boundary, preserve the query dependency-mutation regression, and run the
  focused A\* query slice plus a fresh `Release` coverage aggregate.

### TRB-Issue-024 - A\* publication performs a non-authoritative immediate recheck

- **Discovered:** 2026-09-01
- **Area:** A\* payload publication and guide handoff
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationAStarPayloadCache.TryPublish` validates the exact
  graph dependencies and optional world epoch before publishing. Query work
  immediately repeated those lock-free reads and removed the payload if they
  changed, but the world or graph could still change immediately after that
  second check. Definitive guide creation and guide use already revalidate the
  same proof.
- **Impact:** The best-effort duplicate check adds a race-only state-machine
  path without establishing a stronger currency guarantee.
- **Expected:** Treat cache publication as proof that the payload was current at
  publication time and leave subsequent currency validation to the guide
  consumer boundary that can act on it.
- **Verification:** Preserve cache publication, stale guide acquisition, and
  concurrent guide-use regressions; then run the focused A\* concurrency slice
  plus a fresh `Release` coverage aggregate.

### TRB-Issue-025 - Graph terminal cleanup ownership proof was incomplete

- **Discovered:** 2026-09-01
- **Area:** Graph runtime publication and retained-work ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** An ownership-based cleanup removed post-frame capacity and
  terminal rollback paths as allegedly duplicate. The first combined `Release`
  aggregate falsified that proof: exact-peak materialized and explicit-closure
  operations rejected, and a one-byte-short shrinking replacement retained a
  composition worker instead of rolling it back.
- **Impact:** Omitting the runtime's final ownership cleanup can reject work at
  its exact configured peak, retain operation-owned composition state after a
  terminal frame, and leave structural scopes closed beyond their operation.
- **Expected:** Retain the final deferred-capacity, terminal cleanup, closure
  rollback, lifecycle-result, and composition-result checks unless every owner
  can transfer the same state atomically.
- **Verification:** Restore the established exact-peak materialized,
  explicit-closure, and shrinking-replacement regressions; keep the new natural
  page-boundary tests only where their measured premise is valid; then run the
  focused graph slices plus a fresh `Release` aggregate.

### TRB-Issue-026 - Grid ingress exposes an impossible untracked-scope fallthrough

- **Discovered:** 2026-09-01
- **Area:** Pathing graph grid-change ingress scope accounting
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Every retained linked GridForge event is passed to `TrackScope`
  when it enters the ingress queue. `UntrackScope` exits before scanning when
  tracking has escalated to the global scope, while keyed coalescing preserves
  the event's immutable configuration and grid-generation identity. Therefore
  every non-global event removed from the queue must find exactly one matching
  tracked scope; the method's silent fallthrough has no constructible caller
  state.
- **Impact:** The fallthrough advertises a recoverable state that cannot arise
  through the ingress lifecycle and would silently tolerate corrupted scope
  accounting if its ownership invariant were ever broken.
- **Expected:** Collapse the zombie fallthrough to a diagnostic invariant while
  preserving counted scope removal and global-overflow behavior unchanged.
- **Verification:** Keep ingress coalescing, counted-scope, and global-overflow
  lifecycle tests, then confirm the diagnostic-only fallthrough is absent from
  the next `Release` coverage aggregate.

### TRB-Issue-027 - Endpoint ranking contains an impossible same-address medium tie

- **Discovered:** 2026-09-01
- **Area:** Pathing endpoint resolution and canonical candidate ranking
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Covered-address enumeration emits each canonical configuration
  and voxel once. `ConsiderCandidate` deterministically selects one resolution
  medium from that immutable node, profile, and requested-media tuple, while the
  blocked-ray volume fallback retains the pending candidate's same address.
  Therefore a second candidate cannot reach `CanBeatCurrentResult` with the same
  `NavigationCellAddress` and a lower resolution medium; that tie arm has no
  constructible caller state.
- **Impact:** The zombie tie implies that one canonical address can resolve to
  competing media across endpoint candidates, obscuring the actual stable
  ordering contract and adding an untestable branch to deterministic ranking.
- **Expected:** Rank equal-distance candidates solely by canonical address; keep
  the deterministic per-node medium choice inside candidate qualification.
- **Verification:** Preserve the overlapping-endpoint canonical-selection and
  mixed-medium qualification regressions, then confirm the removed tie arm is
  absent from the next `Release` coverage aggregate.

### TRB-Issue-029 - Navigation trace mapping repeats an impossible map-directory miss

- **Discovered:** 2026-09-01
- **Area:** Ordered navigation-ray and swept-volume trace mapping
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationWorldGraph.TryGetMapId` reads the immutable
  configuration index built from the graph's map instances. Every graph
  transformation either preserves that index with the same instance directory or
  constructs both from the same replacement instance set. A map id returned by
  that index therefore cannot be absent or null in the same graph's instance
  directory, yet both centerline interval mapping and swept-volume mapping
  treated that state as a recoverable stale trace branch.
- **Impact:** The duplicate lookup failure advertises an impossible partial
  graph state and obscures the real staleness boundary: GridForge world identity
  and change-sequence checks against an otherwise coherent immutable graph.
- **Expected:** Keep the configuration-index miss for physically traced grids
  with no navigation map, assert the index-to-directory ownership invariant, and
  retain all real world/grid generation mismatch checks.
- **Verification:** Preserve unmapped-grid, swept-volume, and stale-world ray
  regressions, then confirm the impossible same-graph directory misses are
  absent from the next `Release` coverage aggregate.

### TRB-Issue-030 - Explicit replay repeats an impossible exit-anchor suffix check

- **Discovered:** 2026-09-01
- **Area:** Ordered navigation-ray explicit corridor replay
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Replay starts geometry validation only when the complete ray
  segment contains the connection's exit anchor. Publication validates that
  anchor inside the destination cell prism. A successful final portal replay
  reaches the directed entry point into that same convex prism along the same
  ray, so the remaining portal-to-end segment must contain every collinear
  in-prism point, including the already-qualified exit anchor. The final suffix
  test therefore cannot fail after successful replay.
- **Impact:** The duplicate test advertises a second, contradictory exit-anchor
  failure after the corridor has already established all of its premises and
  adds an unreachable branch to a high-risk path.
- **Expected:** Keep initial entry/exit containment, every witnessed portal and
  body-segment check, and final target-node validation; remove only the
  impossible repeated exit-anchor suffix test.
- **Verification:** Preserve off-line exit, corner-clipping, multi-witness, and
  selected-edge regressions, add a valid turning-corridor shortcut rejection,
  then confirm the duplicate suffix branch is absent from the next `Release`
  aggregate.

### TRB-Issue-031 - Volume-ray allocation gate warms below the tiering boundary

- **Discovered:** 2026-09-01
- **Area:** Volume navigation-ray steady-state allocation regression
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The full instrumented `Release` aggregate reported 5,168 bytes
  during the 64-iteration measurement window after only eight warmup samples,
  while the same test passed both alone and alone under the coverage collector.
  The production result remained `Success` for every measured sample.
- **Impact:** Runtime tiering can begin during the measured window depending on
  prior suite execution, making a real zero-allocation contract intermittently
  fail without any product allocation regression.
- **Expected:** Warm the exact volume-ray path through at least one complete
  measurement-sized window before recording current-thread allocations, while
  retaining the strict zero-byte assertion over the following window.
- **Verification:** Run the focused allocation test normally and under the
  coverage collector, then require repeated full `Release` coverage aggregates
  to retain the exact zero-byte assertion.

### TRB-Issue-032 - Endpoint policy validation repeats impossible successful-null checks

- **Discovered:** 2026-09-01
- **Area:** Endpoint dependency currentness and immutable area-policy ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationAreaCatalog` owns a private `NavigationAreaPolicy[]`.
  Its only successful `TryGet` path assigns the exact array element to the out
  parameter; every miss assigns `null` and returns `false`. Catalog construction
  and publication accept non-null policies and never store a null element.
  Therefore neither the expected nor current policy can be null after its
  corresponding lookup succeeds.
- **Impact:** The redundant null arms advertise an impossible partially-null
  immutable catalog and add untestable branches to endpoint stale-proof
  validation.
- **Expected:** Preserve expected/current lookup failure and policy
  `ContentEquals` rejection, but rely on the successful-lookup ownership
  contract for non-null policy values.
- **Verification:** Keep endpoint policy-replacement and dependency-staleness
  regressions, then confirm the successful-null branches are absent from the
  next `Release` coverage aggregate.

### TRB-Issue-033 - Default baseline rebuild carries impossible capacity and counter fallbacks

- **Discovered:** 2026-09-01
- **Area:** Default-map covered-address baseline reconstruction
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A default baseline rebuild constructs its covered-address cursor
  with generation capacity one and always begins it with an exact eligible
  generation count of one; GridForge documents begin failure only when the
  declared count does not fit the cursor. During discovery, each unique omitted
  seed increments one pending seed count, and each physically present omitted
  seed increments one pending physical count. The unique covered-address cursor
  can rediscover each such seed only once, so either corresponding count must be
  positive when it is consumed.
- **Impact:** Recoverable fallbacks for an impossible cursor-capacity failure
  and impossible zero-valued ownership counters obscure the exact rebuild state
  machine and create untestable branches in safety-critical baseline recovery.
- **Expected:** Express all three states as diagnostic ownership invariants
  while preserving the exact cursor begin, unique rediscovery, structural-state
  removal, and count consumption behavior.
- **Verification:** Preserve default baseline seed, sparse rediscovery,
  capacity, and interleaved-mutation regressions, then confirm the impossible
  fallback branches are absent from the next `Release` coverage aggregate.

### TRB-Issue-034 - Unmatched structural-link deltas expose impossible nonpositive guards

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam structural-link journal application
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `AddLinkDelta` removes a journal entry when both of its counts
  return to zero. A negative count can only remove a link represented in the
  immutable source row. Therefore, when source enumeration is exhausted or a
  journal key sorts before the current source key, that unmatched delta must be
  positive; a nonpositive value contradicts the source-index ownership that
  produced the removal delta.
- **Impact:** The two recoverable guards imply that an unmatched negative delta
  can be silently discarded, obscuring the canonical merge invariant and adding
  branches that no valid seam lifecycle can exercise.
- **Expected:** Assert positive unmatched deltas diagnostically and emit their
  structural-link rows directly, while preserving matched removal and
  replacement handling.
- **Verification:** Preserve removal, replacement, cancellation, and canonical
  structural-link merge regressions, then confirm the impossible guards are
  absent from the next `Release` coverage aggregate.

### TRB-Issue-035 - Retained materialized work exposes an impossible absent closure baseline

- **Discovered:** 2026-09-01
- **Area:** Graph runtime materialized-component publication ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Materialized component work is retained only after
  `CaptureOwnedStructuralClosureBaseline` records the current closure root and
  the owned closed graph publishes successfully. Every cleanup path releases the
  materialized worker before clearing that baseline. Consequently retained work
  cannot reach completed publication without its owned baseline, yet the
  publication path fell back to the empty component set when it was null.
- **Impact:** The fallback advertises a partially initialized ownership state
  and could hide lifecycle corruption by reopening against the wrong closure
  baseline instead of exposing the violated invariant.
- **Expected:** Assert baseline ownership diagnostically and publish completed
  materialized work against the exact retained baseline.
- **Verification:** Preserve retained materialization, closure rollback,
  automatic-seam lifecycle, and exact-capacity regressions, then confirm the
  successful-null fallback is absent from the next `Release` coverage aggregate.

### TRB-Issue-036 - Traversal paths repeat impossible same-graph reference resolution

- **Discovered:** 2026-09-01
- **Area:** Traversal enumeration, native state lookup, and volume-edge
  certification
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Traversal and incoming enumerators receive medium states
  resolved from the same immutable graph they retain. Native and complete
  neighbor lookups, active seam rows, and explicit-route dependency evidence
  likewise produce nodes owned by that graph. Volume-edge endpoints therefore
  retain exact addresses and bound cell prisms for the evaluator lifetime.
- **Impact:** Re-resolving those owned references as recoverable misses creates
  silent empty, stale, or skipped-edge fallbacks for states that valid
  production ownership cannot produce, while encouraging hollow invalid-ref
  tests.
- **Expected:** Assert same-graph address, node, and prism ownership
  diagnostically; preserve legitimate medium, closure, presence, dependency,
  ordering, and budget outcomes.
- **Verification:** Preserve native, seam, transition, Flow, Ray, capacity, and
  blocked-endpoint regressions; remove the invalid-ref enumerator test; confirm
  the repeated-resolution branches are absent from the next `Release` coverage
  aggregate.

### TRB-Issue-037 - Transition refresh exposes impossible source and sort states

- **Discovered:** 2026-09-01
- **Area:** Transition definition publication and bounded page sorting
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Effective-definition cursors enumerate from an existing map in
  the exact graph passed to them, and map authoring rejects every transition
  whose local source cell is absent. Page preparation initializes both merge
  sort workers before page sealing can advance either worker.
- **Impact:** Recoverable missing-owner, missing-source, and uninitialized-sort
  branches imply partially authored or partially prepared transition state that
  the publication lifecycle cannot create.
- **Expected:** Create published transition source pages from asserted owner and
  source-slot invariants, retain optional cross-map destination resolution, and
  treat only zero- or one-record initialized sorts as immediate completion.
- **Verification:** Preserve dormant cross-map destination, transition refresh,
  canonical ordering, retained-byte, and bounded-resumption regressions, then
  confirm the impossible branches are absent from the next `Release` coverage
  aggregate.

### TRB-Issue-038 - Dependency currentness repeats immutable index identity checks

- **Discovered:** 2026-09-01
- **Area:** Surface-component and semantic-page dependency validation
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Surface component records are added to their index under each
  record's exact immutable key, so a successful key lookup cannot return a
  record with a different key. `TryGetPageDependency` already performs the same
  map lookup that preceded it, and the preceding instance value was unused.
- **Impact:** Duplicate identity predicates add unreachable mismatch branches
  without strengthening stale-proof validation and obscure the meaningful
  closure, missing-record, version, map, and page comparisons.
- **Expected:** Assert component-key ownership diagnostically, perform each map
  lookup once, preserve the null-stamp guard, and retain all meaningful
  dependency-currentness failures.
- **Verification:** Preserve component closure/version, missing map/page, area
  policy, and transition-version regressions, then confirm duplicate identity
  branches are absent from the next `Release` coverage aggregate.

### TRB-Issue-039 - Materialized work repeats immutable graph ownership fallbacks

- **Discovered:** 2026-09-01
- **Area:** Materialized grid-event capture and seam publication
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A candidate graph's configuration index is constructed from the
  same immutable instance directory, so a successful map-ID lookup cannot miss
  that map in the directory. A materialized seam refresh requires a live world;
  its transition refresh publishes `_transitionGraph` earlier in the same
  `Advance` call before seam results can create the component graph.
- **Impact:** The duplicate directory miss and candidate-graph fallback imply
  inconsistent immutable graph storage or out-of-order publication that the
  materialized lifecycle cannot produce.
- **Expected:** Keep unrelated event configurations as ordinary misses, assert
  index/directory and transition-before-seam ownership, and publish from the
  exact transition graph.
- **Verification:** Preserve materialized event, transition, seam, snapshot
  restart, and retained-work regressions, then confirm both impossible fallbacks
  are absent from the next `Release` coverage aggregate.

### TRB-Issue-040 - Baseline rebuild repeats an impossible configuration-key mismatch

- **Discovered:** 2026-09-01
- **Area:** Chunked physical-baseline identity validation
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** GridForge's `GridWorld.TryCaptureNavigationBaselineCore`
  resolves the active grid by the exact requested `GridConfigurationKey`, then
  constructs the successful `GridNavigationBaseline` with that same
  `configurationKey` value verbatim. A baseline returned for the map binding
  therefore cannot carry a different configuration key.
- **Impact:** Rechecking the successful baseline against the request adds an
  unreachable mismatch branch without detecting world, slot, generation, or
  concurrent grid-change invalidation.
- **Expected:** Rely on GridForge's exact-key capture contract while retaining
  the world spawn token, grid index, grid spawn token, and last-change sequence
  identity checks across retained chunks.
- **Verification:** The chunked baseline rebuild suite preserves exact map and
  GridForge generation identity, and both exact coverage aggregates plus the
  Release/ReleaseLean matrix pass.

### TRB-Issue-041 - Strict solid endpoint overflow is overwritten as invalid

- **Discovered:** 2026-09-01
- **Area:** Strict endpoint candidate qualification
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A representable point inside a maximal rectangular prism can
  have an unrepresentable distance from the cell's solid foot anchor.
  `TryQualifyCandidate` records `CostOverflow`, but the failed solid
  qualification previously returned `true` because it consumed no volume work;
  cursor completion then replaced the terminal result with `InvalidEndpoint`.
- **Impact:** Valid mapped endpoint geometry reported the wrong failure status,
  hiding deterministic fixed-point cost overflow from callers.
- **Expected:** Stop candidate enumeration whenever qualification has already
  set a terminal status, while preserving ordinary non-qualifying solid and
  volume candidate behavior.
- **Verification:** Keep the maximal-prism strict endpoint one raw unit inside
  the authored cell, assert exact `CostOverflow` and candidate/page accounting,
  then run focused and full `Release` coverage aggregates.

### TRB-Issue-042 - Transition-rule seam scans retain unreachable completed-state guards

- **Discovered:** 2026-09-01
- **Area:** Incoming and outgoing transition-rule volume seam enumeration
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Entering either enumerator's volume-seam phase initializes
  completion to false. The only path that sets completion true immediately flows
  into `CompleteRuleContactScan`, which advances the rule and resets the phase
  before the enumerator can re-enter that scan state.
- **Impact:** Rechecking the completed flag on phase entry modeled an invalid
  retained enumerator state and added a branch with no production transition.
- **Expected:** Advance seam lookahead whenever none is owned, assert that the
  active phase is not already complete, and preserve bounded edge-budget,
  lookahead, and canonical predecessor behavior.
- **Verification:** Preserve native/seam predecessor ordering, blocked budget,
  and transition-rule scan regressions, then confirm the duplicate predicate is
  absent from the next full `Release` coverage aggregate.

### TRB-Issue-043 - Transition guide allocation gate is sensitive to runtime tiering

- **Discovered:** 2026-09-01
- **Area:** A\* transition-guide zero-allocation regression
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The full instrumented `Release` aggregate reported 3,248 bytes
  inside the single measured window of
  `AStar_ShouldReconstructExplicitTransitionWithoutTreatingItAsMovement`, while
  five isolated instrumented reruns of the same test reported zero. The guide
  behavior remained successful in every run, and the test already performed a
  fixed warmup before measuring the identical call loop.
- **Impact:** A one-time runtime or coverage-instrumentation transition can fail
  the release gate even though the post-transition hot path is allocation free,
  making the test order-sensitive instead of measuring steady state.
- **Expected:** Require a zero-allocation steady-state window after bounded
  stabilization, while still failing persistent or recurring allocations from
  `TryGetCurrentStep`.
- **Verification:** Run the focused test repeatedly with coverage, then repeat
  the full instrumented `Release` aggregate and confirm the allocation gate and
  coverage totals are stable.

### TRB-Issue-044 - Seam refresh reclassifies an already-owned discovery key

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam map-mode completion and discovery requeue
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A pending discovery key is queued before `BeginMapMode` only by
  the discovery-map `RevalidateMap` path or the replacement-map `RemoveMap`
  path. Direct discovery uses `Discover`, which `AdvanceMode` routes exclusively
  through `AdvanceDiscovery`, and every completion, failure, and reset path
  clears the queued key.
- **Impact:** Rechecking the completed mode as `RemoveMap` or `RevalidateMap`
  models a phase/key ownership state the worker cannot create and leaves an
  unreachable branch in the lifecycle state machine.
- **Expected:** Assert the queued key's map-mode ownership diagnostically and
  requeue it directly whenever it is present.
- **Verification:** Preserve removal, replacement, discovery, stale restart, and
  blocked-budget regressions, then confirm the redundant classifier is absent
  from the next full `Release` coverage aggregate.

### TRB-Issue-045 - Seam discovery rechecks an impossible completed output

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam boundary-contact discovery
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Seam discovery calls GridForge boundary-contact advancement with
  an output limit of one. GridForge returns `More` immediately after emitting
  that one pending contact, so a returned `Complete` status necessarily owns an
  output count of zero.
- **Impact:** Combining `Complete` with a second zero-output predicate models a
  cursor result the bounded producer cannot return and leaves an unreachable
  branch in the discovery state machine.
- **Expected:** Assert the producer contract diagnostically and handle
  `Complete` directly, while preserving one-contact resumability and all
  completion side effects.
- **Verification:** Preserve blocked, one-contact, stale, and completed seam
  discovery regressions, then confirm the redundant predicate is absent from the
  next full `Release` coverage aggregate.

## Verification Record

### 2026-09-01 coverage hardening

- `Release`: 2,288 tests passed; 30,013 of 30,013 lines, 11,903 of 11,903
  branches, and 2,952 of 2,952 methods were covered. All 2,952 methods were
  fully covered.
- `ReleaseLean`: 2,239 tests passed with the same 30,013-line, 11,903-branch,
  and 2,952-method production surface at exact coverage.
- The serial full-solution restore, build, and test matrix passed in both
  configurations. Each build completed with zero warnings and zero errors.
- Pull-request validation now enforces exact coverage independently for the
  Ubuntu `Release` and `ReleaseLean` jobs. Post-main coverage runs only after a
  successful build workflow and checks out that workflow's exact head SHA.
- The final reviewer pass found no tracked generated, build, or coverage
  artifacts.

## Validation Workflow

- Use package references for normal development and release validation.
- Use `UseLocalLsfStack=true` only when an unreleased lower-stack change must be
  validated across sibling repositories.
- Pair `UsePrebuiltLocalLsfStack=true` with `UseLocalLsfStack=true` only for
  benchmark child builds that consume an already-verified local stack.
- Resolve defects in the repository that owns the behavior, then release and
  validate packages in dependency order.
- Before release, run Trailblazer `Release`, `ReleaseLean`, coverage,
  allocation, and relevant benchmark gates against package dependencies.
- Keep volatile test and coverage counts in generated reports or dated
  verification records rather than this active section.
