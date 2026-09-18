# Navigator Acquisition And Recovery

**Created:** 2026-09-16

**Status:** Complete, user-reviewed and archived on 2026-09-18. The user approved
squashing the reviewed `e5d29e0` feature into `develop` and resuming
`TRB-Benchmark-005`. The squash commit containing this archive is the accepted
recovery checkpoint; no remote push or release is implied.
On 2026-09-18 the user authorized Trailblazer feature-branch commits and indicated
that a modest performance cost for hardened behavior is an acceptable tradeoff.
Do not keep this feature in an indefinite optimization loop to recover that cost;
further steady-state and recovery-efficiency work belongs to `TRB-Benchmark-005`.
This is a change in acceptance direction, not proof that all measured cost comes
from correctness fixes or is bounded by 2–3%. The latest GridForge registry
comparison remains **inconclusive for performance**, with every candidate median
between drifting corrected-parent controls. Its ownership/retention correction
is now committed upstream as `9c712ec`; no speedup or equivalence is claimed.
Fresh Windows/Linux Release/Lean full tests pass for GridForge and Trailblazer,
with exact 100% line/branch/method coverage collected on Windows. Gravitas's full
Windows Release/Lean suites and both repositories' documentation gates also pass.

The earlier full Windows/Linux, exact-coverage and documentation checkpoint is
green. Native debugging explains the exact 6,008-byte allocation-counter failure,
and the strict measurement correction is retained. Separately, an original
baseline setup threw inside GridForge interval sorting; subsequent clean runs
and a bounded native-debugger batch do not explain that failure. It remains
unresolved. It has no established causal connection to the recovery changes and
remains a separate baseline/setup observation, not a requirement for indefinite
identical reruns before feature review. No crash fix or benign-cause claim is made.
Historical failures, rejected experiments and their evidence remain below;
current disposition supersedes their earlier blocking status, not their results.
This plan, not a standalone tracker entry, owns regressions introduced here.

**Execution branch:** `codex/navigator-recovery`, created from `develop` at
`ff13eee5c90d913ce718652daa55f9dbd6361fa5` in the existing checkout and reviewed
at `e5d29e0`. The user approved its squash into `develop` on 2026-09-18.

**Coordination:** [TRB-Benchmark-005](../benchmark-signal-hardening-backlog.md#trb-benchmark-005---guided-frames-spend-most-time-in-long-blocked-segment-tracing).
Keep design decisions, implementation progress and condensed results here.
Raw captures belong in ignored artifacts, not another supporting document.

**Working baseline:** Trailblazer `ff13eee` on `develop`; GridForge `4a6b1bd`,
Gravitas `aa48f7c`, FixedMathSharp `29bfa28`, SwiftCollections `4b5715b`.
All five checkouts were clean when the diagnostic was captured. Use the existing
checkout and local source stack; do not stage, commit, push or release without
the corresponding user request.

## Goal And Decisions

Make simultaneous Navigator acquisition and recovery affordable without hiding
the cost in an impractically long queue. Track three separate outcomes:

1. Complete fixed-frame cost, against the **2 ms navigation allowance** and
   **31.25 ms full-frame ceiling** at 32 Hz.
2. Total acquisition/recovery work, including repeated search and adoption work.
3. Fixed frames until the first, median and last agent have usable guidance.

Scheduling alone does not complete `TRB-Benchmark-005`. Ordinary guided frames
also still miss 2 ms; this slice targets recovery spikes, not every remaining
controller/sampling cost. No portable timing guarantee is implied.

The user approved **opt-in Navigator scheduling**: existing callers retain
immediate acquisition; waiting Navigators still undergo normal gravity, platform
and host motion. The user also requires a **smart existing queue**: merge
compatible work and process multiple requests in a frame when the shared work
allowance permits. Do not bolt a second queue onto Navigator.

### Alternatives Considered

- **Recommended: extend the existing scheduler and acquisition workers.** One
  queue, bounded shared work, compatible payload reuse, explicit Navigator
  ownership and current-position adoption. This addresses both spikes and
  queue latency without creating a second search implementation.
- **Only replace synchronous requests with `Schedule`.** Smaller patch, but
  leaves mandatory direct checks synchronous and adds avoidable latency. It
  also does not define what happens when the host moves while waiting.
- **Introduce a general job system or persistent resumable Flow cache.** More
  machinery than the current evidence justifies. Neither is required for the
  first implementation; do not add workers, priorities or speculative caches.

## Evidence And Existing Constraints

The existing `guided-phase-smoke` passed on the baseline source stack. Its
Flow/100, 100-by-100 wall-gap scene produced the following **single-run
diagnostics**, not repeated performance acceptance:

| Containing operation | Observed time |
| --- | ---: |
| Initial admission | 514.013 ms |
| Same-map rebake and ordered reacquisition | 310.131 ms |
| Following-frame pending retry | 259.267 ms |
| Profile replacement admission | 268.076 ms |
| Post-restore admission | 13.122 ms |

The frame measurements include context simulation, all 100 Navigator simulations
and motion commits, and instrumentation. JSON restore preparation is separate
(242.701 ms), not a navigation frame. Resume with retained guides is 15.171 ms;
acquisition scheduling does not eliminate that overdue periodic-LOS burst.

A separate thread-sampled trace restricted to `CaptureBurstFrame` plus
`SimulateAgentsInstrumented` attributes about **78.75%** of sampled later-frame
time to Flow search, **4.98%** to direct heading, **0.85%** to other Flow request
work, and **15.42%** to other controller work. This is a coarse locator, not
precise CPU attribution, a count of cold builds, or a speedup prediction. It
excludes the initial admission and JSON preparation; do not mix its denominator
with the table above.

Evidence: `artifacts/benchmark005-recovery-design/` contains the build/smoke
logs, source-state and runtime SHA256 manifests, retained assemblies, sampled
trace and stack summary. Stopwatch frequency was 10,000,000 ticks/second.

Baseline source inspection established these constraints:

- `NavigationGuideScheduler.Advance` currently advances only one phase per
  context frame: activation, admission, search, publication. Even a warm
  successful request needs at least four frames. Five hundred successful
  independent tickets therefore need at least 2,000 frames (62.5 simulated
  seconds), excluding additional search/retry frames.
- Request retention and active guide leases are separate finite capacities.
  Defaults are not a 500-agent configuration. Completed unclaimed tickets
  retain capacity; cancellation and transfer must remain exactly once.
- `TryGetDirectHeading` currently runs outside the scheduler. Moving just
  `RequestGuide`/`RequestFlowField` leaves synchronous admission rays behind.
- Flow keys already exclude origin but include destination, resolved destination,
  profile, policy, media, budget and options. An uncovered origin starts a new
  search from the destination; a longer result can replace the cached prefix.
  A stale incumbent is removed and reported stale to that caller. The next
  caller can rebuild. The rebake/retry capture is consistent with repeated
  prefix work, but does not establish the number of builds or their callers.
- Query-meter deltas are **not** consumed scheduler slices: workspace clearing,
  reconstruction, copying and other postprocessing consume slice units without
  incrementing expanded-node totals. Multiplying or silently refilling slices
  while draining several requests would violate the intended frame allowance.
- Flow sampling can certify a changed current foot. A* has no current-foot
  rejoin API: lease status only validates the payload, not the host's position.
  An exact-foot discard/retry rule alone can starve a continuously moving host.

## Implementation Contract

### One Bounded, Work-Conserving Queue

Retain context ownership, deterministic submission order, existing search
workers, total query budgets and public ticket disposal/transfer semantics.
Replace the compulsory one-phase-per-frame pacing with progress while a single
**remaining allowance for that context frame** permits it. Skip empty phases;
do not grant a new full slice when changing phase, algorithm or request.

Carry actual remaining counted work through admission, search and
postprocessing. Add one finite operation ceiling for phase transitions,
cache-hit completions and atomic work that does not spend those counters.
Queue scanning, grouping, result fan-out and adoption validation must also be
bounded. Exhaustion yields to the next context frame. No wall-clock deadlines,
threads, tasks, random ordering, polling loops without progress or new framework.

These are work limits, not elapsed-time bounds. A ray, allocation, cache
validation or publication can still be expensive. Include all of them in the
containing-frame measurements. Default settings and tuned 100/500-agent settings
must be reported separately; do not conceal a throughput improvement by silently
increasing budgets or retention. Public scheduled requests may complete sooner;
Navigator immediate mode retains its existing timing behavior.

### Share Compatible Work, Not Agent State

Use the existing payload keys as the final equivalence authority, under the
same valid graph/dependency lifetime. A matching destination string alone is
not sufficient. Keep per-request endpoint admission and budgets authoritative.

- **Flow:** compatible requests may join one in-flight destination-field build.
  Resolve each origin independently. The first implementation keeps the oldest
  request's search extent; followers whose origins are covered receive separate
  guide leases over that payload. Uncovered followers remain pending for their
  own bounded search/promotion; do not pretend a prefix covers the whole map.
- **A\*:** merge only exact admitted request matches. Different starts do not
  share a route just because the destination matches.
- Each member retains independent cursor, medium, transition, cancellation and
  lease ownership. Cancelling one member must not cancel useful shared work for
  the remaining members; cancellation of the last owner releases it.
- Do not sum budgets or let a follower enlarge/fail the leader's original query.
  A failed or unresolved origin does not fail unrelated members. Existing
  capacity ceilings apply to descriptors, members, payloads and returned leases.
- Start with bounded **contiguous compatible cohorts** in submission order.
  Do not scan past unrelated requests to give a later cohort priority. Freeze
  cohort membership when its build starts; later submissions cannot perpetually
  extend active work. Recheck the cache before starting each remaining build.

FIFO governs cohort selection and the order of remaining builds, not strict
completion order within a cohort. A covered follower may become ready while an
earlier uncovered member still needs work. Publish simultaneously ready members
in submission order; retain each unclaimed result's capacity. Do not start the
next unrelated cohort ahead of the current cohort's remaining builds. Document
and test this change from the old one-request-at-a-time completion order.

This is real in-flight sharing and independent result fan-out, not merely a
batch that prepares N cold searches before any payload is published. Existing
batch admission is not automatically a coalescer. Count builds, covered
followers and promotions to demonstrate the implementation's effect.

Extending one live Flow frontier to several uncovered origins is a possible
follow-up only if the measured duplicate work warrants it. Do not introduce
that complexity, persistent frontier retention or request reordering speculatively.

### Navigator Ownership And Waiting

Expose one explicit immediate/scheduled acquisition setting in shared steering,
plus observable waiting/outcome state. A* and Flow use the same setting and
lifecycle; no algorithm-specific configuration or compatibility overloads.
Changing the setting validates first and cancels/restarts transient work at the
next eligible deterministic boundary. Serialize the durable setting, not tickets.

One Navigator owns at most one pending acquisition. Its lifecycle is:

`needs guidance -> queued/working -> validate adoption -> following`

Stale/capacity results yield an explicit later-frame retry; terminal invalid,
unsupported or exhausted-query-budget outcomes use the existing failure policy.
At most one submission/retry attempt per Navigator per context frame. Queue-full
does not lose intent, allocate an unbounded retry list or start synchronous work.

While waiting, produce no new guided heading, no arrival event and no stuck
timeout/repath escalation caused only by the wait. Continue motor, gravity,
platform, collision, occupancy and motion commits. Waiting is not immobilizing
the body or discarding its velocity. Do not use stale guidance to keep moving.
Respect the existing pause and virtual steering-hook boundaries.

Mandatory initial/recovery direct checks must participate in this bounded
acquisition path; do not run them again in every waiting `GetHeading` call.
Public guide requests still promise an A*/Flow lease, not a new public direct
result type. Periodic LOS cadence and the existing opt-in phase policy remain
unchanged; overdue periodic checks are not silently folded into this feature.

### Adopt At The Current Foot

Validate unchanged query intent/profile/medium, ticket ownership and existing
world/dependency freshness before using a result. Retain the scheduler's
activation-to-transfer raw-world fence; after transfer use normal lease
dependency rules, not a new blanket graph-version equality requirement.

- **Flow:** use the first successful current-foot sample as that frame's actual
  steering sample. Do not validate by sampling and then sample a second time.
  `LocalRecoveryRequired` releases the unusable lease and schedules a query from
  the current foot. Exhaustion of the full configured per-call sample budget is
  an observable `BudgetExceeded` failure: release it and use the invalid-path
  stop/notification boundary, not endless sampling or automatic same-query retry.
  Lack of a frame-level adoption allowance merely defers the attempt and is not
  this terminal outcome. Sampling has no failed-sample continuation.
- **A\*:** add a bounded internal current-foot connector proof to the route's
  exact initial anchor (index zero) before adopting a moved-origin result.
  Preserve and test the existing ordinary-start-anchor invariant. Reuse existing
  ray/body/policy authority. Retain the connector's own bounded dependency proof
  in Navigator/lease-local state until that anchor is consumed, and validate it
  before each connector heading. The original payload cannot cover dependencies
  visited only by the connector. Invalidation yields zero heading and recovery;
  cancellation/anchor consumption releases this extra proof. Account for its
  retained bytes. Do not skip waypoints or semantic actions, accept same-cell
  proximity as proof, mutate a shared payload, or indefinitely restart a full
  search merely because the host moves each frame. If a safe connector cannot be
  proved, keep zero guided heading and schedule current-origin recovery.
- **Direct-only result:** a proof from the submitted foot cannot certify a new
  foot. Re-prove against the current position or continue through graph acquisition;
  do not reinterpret the old heading as fresh. A moving clear-path agent must not
  be trapped in an endless queue/reject cycle.

Current-foot validation may need to execute at the Navigator boundary where the
foot is actually known. Charge it to the same context-frame operation allowance,
including when the context has already advanced its queue. A missing allowance
means wait, not unbounded synchronous fallback. Prove that queue work cannot
permanently consume every adoption opportunity. Measure transfer/first-sample
frames explicitly; relocating a spike there is not success.

### Cancellation, Actions And Restore

Cancel/release on stop, arrival, input-mode switch, destination replacement,
successful profile/medium replacement, reset, rebind, disposal and successful
load. No late result may revive an old session. Pending actions remain owned and
completed exactly once through their existing lease; queue polling never executes
or skips an action. Already-stale actions follow the existing failure behavior.

Persist configuration and durable intent only. Restore creates no tickets during
record validation and performs fresh acquisition afterward. Failed transactional
load/profile replacement preserves the live ticket/lease/session. Update the
explicit Navigator schema, JSON/MemoryPack tests and migration notes together;
do not restore queue position, continuations, leases, proofs or timing credit.

## Verification And Measurement

Use existing projects, fixtures and the local source stack. No additional
benchmark/CI project, public telemetry layer or engine adapter is required.

1. **Freeze the comparable recovery harness before production changes.** Keep
   the existing guided steady-state and lifecycle cases intact. Add distinct
   recovery cases in the existing benchmark project for A*/100 and Flow/100/500,
   with identical map, profile, query budgets, agent order and lifecycle trigger
   across **baseline-immediate, candidate-immediate and candidate-scheduled**.
   The baseline has no Navigator scheduled mode. Compare old/new immediate for
   regressions, then candidate scheduled against its same-build immediate control
   for the tradeoff. Include cold, warm, rebake, profile change and restore; keep
   JSON preparation separate. Include cache-hit, mixed-key, uncovered-origin and
   capacity-pressure controls. Verify an empty relevant cache for every cold
   operation and an exact compatible hit for every warm operation, including each
   warmup and actual iteration; setup preflight must not silently warm cold cases.
2. Record every frame from trigger through last usable result, not merely the
   enqueue call or eventual completion frame. Report acquisition-specific work
   counters and build/promotion counts separately from summed **complete host-frame
   elapsed time and allocations**. Waiting adds controller frames; that sum is
   not isolated search cost. Record process CPU-time deltas over the recovery
   window, labeled as process-wide CPU (including background/JIT/GC), not exact
   acquisition CPU; use separately collected root-filtered profiles for attribution.
   Report per-frame median/tails/maxima, allowance misses and readiness frames.
   A direct result is ready only after current-foot proof; a graph result only
   after a usable heading/accepted action or validated arrival, not a non-null
   lease. Track every original request, including failed, cancelled and superseded
   outcomes. Report successful-request percentiles with the denominator, and
   all-agent completion only if all original eligible requests succeeded. Stop
   a diagnostic at 10,000 frames with remaining agents explicitly censored, not
   silently omitted; this safety cap is not an acceptable gameplay-latency target.
   Separate actual elapsed milliseconds from simulated wait at 32 Hz. Retain
   capacity settings; no hidden prewarming that the control did not receive.
3. Use three fresh launches, one warmup and three actual recovery episodes
   per launch, with two matched baseline/candidate captures. Retain
   all observations, binary/source manifests and diagnostic counters. Compare
   the existing steady-state cases separately. Validate movement, guide
   freshness, actions and replay outside timing. Identical histories within one
   mode must replay exactly; delayed and immediate trajectories may differ.
4. Preserve immediate behavior. Test aggregate one-below work limits, no-op
   progress, multiple completions, true sharing versus incompatible requests,
   leader/follower cancellation, unclaimed capacity, raw mutation, publication,
   queue reset/disposal and exact result transfer. Test moving platforms,
   falling, pause/resume, grouped agents, superseded requests, load failure,
   profile changes and semantic actions for both algorithms. Include publication
   that blocks only a newly added A* connector, plus stationary Flow adoption
   failures that must exit waiting rather than repeat an unchanged failed sample.
5. Build `netstandard2.1` and `net8.0`; run focused and full Release/ReleaseLean
   core/adapter tests, exact reachable coverage, allocation/replay gates, DocFX
   and runnable documentation examples. Use SwiftThrowHelper, bounded iterative
   work and separate files for non-private types. Independent correctness and
   Ponytail/evidence review precede completion.

Retain only demonstrated improvements: lower recovery spikes must be considered
alongside aggregate cost and useful-guidance latency. Report 2 ms/31.25 ms misses,
steady-state regressions, extra work and long waits even if the queue behaves
correctly. Performance targets guide engineering judgment, not a pre-implementation
no-go or a claim that an unbuilt feature failed. If the tradeoff is poor, revise
the implemented candidate using evidence or present the result honestly.

## Resolved Implementation Regressions

These corrections belong to this feature, not the evergreen issue tracker.
Previously assigned IDs are retained only to identify the diagnostic history;
they are not separate backlog work. Pre-existing Flow/A* endpoint-proof defects
and host-contact occupancy ordering remain in the issue tracker.

### Successful restore retained an old direct-adoption retry flavor

Historical identifier: `TRB-Issue-143`. Resolved within this feature branch.

- **Discovered:** 2026-09-17 in Task4 review and reproduced during Task5.
- **Evidence:** After a completed direct ticket is blocked at its moved current
  foot, successful JSON/MemoryPack population retained `_graphOnlyRetry`. The new
  Scheduled session submitted graph-only work despite a clear direct route.
- **Resolution:** Successful steering load/session restoration clears that
  transient flavor; failed staged population still preserves live state.
- **Verification:** A*/Flow x JSON/MemoryPack regression failed4/4 before the fix
  and passed afterward. Retained `task-5-restore-red.log` and
  `task-5-bytes-restore-green.log` under `.superpowers/sdd/navigationRecoveryPlan`.

### Adopted connector ownership missed teardown and Immediate-mode boundaries

Historical identifier: `TRB-Issue-142`. Resolved within this feature branch.

- **Discovered:** 2026-09-17 during Task5 connector lifecycle testing.
- **Evidence:** Context disposal retained the adopted route/proof charge; rebind
  after changing to Immediate also kept the old context's connector. A blocked
  connector in Immediate could synchronously retry on a repeated same-frame call.
- **Resolution:** The existing validation interest retains a generation-checked
  alias of its actual connector-owning guide, with no additional lease or pin.
  Registration and alias publication are atomic under the scheduler lock. Reset
  and disposal release that owner; ordinary unregister only clears the alias.
  Rebind checks actual connector ownership, and blocked recovery defers through
  the existing per-frame attempt guard regardless of future acquisition mode.
- **Verification:** Retained RED logs `task-5-teardown-red.log`,
  `task-5-rebind-red.log`, and `task-5-mode-retry-red.log`; corresponding focused
  lifecycle/connector tests cover both mode directions, both advancement paths,
  clock/pathing-only reset, disposal, reused generations and transactional loads.
  Exact capacity tests include the alias's16 logical bytes in the active proof
  charge. Final test and smoke evidence is in `task-5-report.md` under
  `.superpowers/sdd/navigationRecoveryPlan`; this does not close performance or
  final platform/coverage gates.

### Pathing reset left completed Navigator tickets eligible for adoption

Historical identifier: `TRB-Issue-141`. Resolved within this feature branch.

- **Discovered:** 2026-09-17 in independent Task4 review; reproduced in the fix round.
- **Evidence:** Pathing-only reset preserves the clock lifetime and a completed
  ticket's public Success outcome, but releases its scheduler/lease ownership.
  Steering subsequently registered validation interest for the released ticket.
  A*/Flow graph transfer failed while still reporting Success, producing invalid
  and arrival notifications and clearing query/group intent. Direct tickets could
  instead revive the old acquisition after reproof. Eight regression cases cover
  both algorithms, graph/direct tickets, and reset before/after interest registration.
- **Resolution:** Steering checks scheduler ownership before status/adoption handling.
  Lost ownership yields explicit Cancelled waiting and marks the current attempt
  frame; replacement cannot submit until a later frame. The public ticket outcome
  contract is unchanged.
- **Verification:**
  `PathingResetOfCompletedTicket_ShouldRetryWithoutRevivingResultOrLosingIntent`
  failed8/8 before the fix and passed8/8 afterward. Tests republish a valid graph,
  preserve query/group/intent without events, prohibit orphan registration and
  same-frame replacement, then require eventual fresh adoption. RED/GREEN logs:
  `.superpowers/sdd/navigationRecoveryPlan/task-4-fix1-{red,green}.log`.

### Policy opt-in missed a clock reset before its first heading

Historical identifier: `TRB-Issue-140`. Resolved within this feature branch.

- **Discovered:** 2026-09-17 during Task4 lifecycle self-review.
- **Evidence:** Adopt an Immediate direct route, change to Scheduled, reset the
  context before the first Scheduled heading: the heading incorrectly remained
  `(1,0,0)` from the old direct proof instead of waiting. The lifetime was only
  captured lazily by the first Scheduled heading.
- **Resolution:** Capture the context lifetime at the Scheduled policy boundary.
  Existing usable guidance remains usable until a real lifetime change.
- **Verification:** `SwitchingAdoptedDirectGuidanceToScheduled_ShouldRememberLifetimeBeforeFirstHeading`
  failed before the fix and passed after it; retained RED/GREEN logs are under
  `.superpowers/sdd/navigationRecoveryPlan/task-4-mode-lifetime-*.log`.

### Direct-only policy change stayed deferred indefinitely

Historical identifier: `TRB-Issue-139`. Resolved within this feature branch.

- **Discovered:** 2026-09-17 during Task4 lifecycle tests.
- **Evidence:** Changing a pending direct-only Scheduled request to Immediate
  cancelled the ticket, but `CanPathfind=false` bypassed the validation method
  that cleared the deferred restart, so every later heading remained zero.
- **Resolution:** Clear elapsed-frame policy deferral at the heading entry point,
  before pathfinding capability dispatch; same-frame restart is still forbidden.
- **Verification:** `DirectOnlyModeChangeToImmediate_ShouldResumeAfterOneFrame`
  reproduced the zero-heading failure before the fix and passes afterward.

### Scheduled phased acquisition repeatedly cancelled unarmed work

Historical identifier: `TRB-Issue-137`. Resolved within this feature branch.

- **Discovered:** 2026-09-17 in the first Task4 scheduled Flow smoke.
- **Evidence:** 100 requests remained pending for 10,000 frames with zero search
  builds. The existing periodic phase lifetime check interpreted an unarmed
  periodic schedule as a clock reset and cancelled each in-flight ticket.
- **Resolution:** Scheduled mode uses its own acquisition lifetime check before
  steering and arms periodic LOS only after usable guidance adoption.
- **Verification:** `ScheduledPhases_ShouldNotCancelUnarmedInitialTicketEveryFrame`
  reproduced missing guidance, then passed after the lifetime ownership fix.
  The original censored smoke record is retained in the ignored task evidence.

## Blocking Branch Regressions

This is the chronological investigation record. See the current status above and
the review handoff below for disposition; earlier failed gates remain evidence,
not an instruction to repeat resolved work or ignore the user's later direction.

The `develop` baseline passed Release (2,662 core + 65 adapter tests) and
ReleaseLean (2,590 + 65). These regressions appeared during this feature's work
and remain its responsibility unless independent evidence establishes otherwise.
An unidentified cause alone does not make a failure external. The later sorting
failure occurred in the unchanged frozen baseline; its cause is still unknown.
Earlier standalone tracker classifications were
withdrawn on the user's direction; all raw evidence is retained.

### Adoption Sample Freshness Correction

A review found another branch correctness defect while investigating the gates:
Scheduled Flow adoption retained a successful first sample across host hooks
without rechecking its inputs. A hook could publish a route change, reset the
payload cache or supply a different sampling position, yet receive the old
positive heading. The fix records the origin and pre-sample graph/world versions
in the existing lazy holder. Consumption clears the one-use value first and
requires matching inputs plus a live successful lease. The initial correction
fell back to another bounded sample, but review caught that this contradicted
the one-sample adoption-heading contract. The revised behavior defers the
heading until the next context frame while retaining the lease. Normal lease
dependency validation then determines whether to sample or recover; an unrelated
graph revision does not synthesize staleness or force a rebuild. Unchanged inputs
still reuse the first sample without additional capacity or transition progress.

Final tests cover relevant publication, raw mutation, cache reset, redirected
origin, unrelated publication, same-frame waiting, mode switches and overridden
heading/action output. They prove next-frame reuse without another build for a
current lease, normal recovery for a stale lease, no stuck/arrival progression
while waiting, and complete teardown. The origin test reproduced an incorrect
same-heading arrival; two additional RED cases exposed discarded guidance on
mode switch and leaked action output from a hook. A synthetic-Stale mutation
fails all three unrelated-publication cases, proving that the suite rejects
blanket lease invalidation. Final focused runs pass 307 Release and 263 Lean
cases, followed by the full normal matrix below. Independent final source/test
review found no actionable issue. Evidence is retained under
`artifacts/navigator-recovery/adoption-defer-*` and `adoption-one-sample-red*`.
The holder gains 40 bytes of fields on first Scheduled Flow use; historical
first-use measurements below predate this correction. Epoch metadata remains
off the ordinary Immediate path; fresh performance comparison and exact
coverage are separate required gates.

### Connector Proof Can Become Stale Inside A Direction Hook

- **Status:** Corrected with executable RED/GREEN evidence; fresh whole-branch
  verification remains required.
- **Boundary:** An A* connector is proved before the virtual validation/direction
  hooks. The later ordinary step read validates the shared route and endpoint
  proof, but not the connector's separate current-foot dependencies.
- **Reproducer:** In the existing route x96→97 / moved-foot x0 fixture, publish
  a cost change on connector-only page 0 inside a one-shot `FindTargetDirection`
  override, then delegate unchanged to the base method. The route remains current
  while the connector becomes stale. All three new publication cases returned
  an incorrect positive-X heading before the correction: initial adoption,
  recurring validation, and a hook replacing the base result.
- **Correction:** A position-aware internal step read validates generation,
  shared payload and the retained current-foot proof under the existing lease
  lock. Ordinary guides do not acquire a second lock or walk connector
  dependencies. Both base-direction step reads honor refusal before processing
  a step or action. A stale local proof retains intent and its still-current
  shared route; a disposed/stale guide follows normal retry. The heading is
  deferred in the hook's resulting context frame, before another connector turn
  can be consumed. No new ray, proof replacement or cached authority is added.
- **Verification:** Six new cases cover the three publication variants, raw
  mutation, pathing reset and redirected base origin. They assert zero heading
  and action, unchanged cursor and stuck checks, no false invalid/arrival event,
  no ray work on repeated same-frame calls, and later charged recovery without
  rebuilding the shared route. An override replacing both refused heading and
  action is still suppressed. Final focused runs pass **307 Release / 293 Lean**
  cases with zero failures/skips or build warnings. Evidence is retained under
  `artifacts/navigator-recovery/connector-hook-freshness/`, including the original
  three-failure RED and subsequent fixture/compile corrections. The raw-world
  fixture's route correctly stays current because only the connector is affected.
  Independent source/test re-review found the original Important finding addressed
  and no remaining actionable issue; timing and full-suite acceptance are separate.
- **Coverage follow-through:** The first refreshed full matrix passes 3,024
  Release / 2,946 Lean core tests and 65 adapter tests in each configuration,
  but exact core coverage identifies two lines and three branch outcomes still
  missing. Three additional behavior tests cover an unbound position-aware
  handle, first-use Immediate deferral after disposal inside the target hook,
  and disposal after anchor advancement but before the second step read. The
  last test coordinates the real connector-proof release using the existing
  cache lock; no production test hook or probabilistic race is added. Focused
  checks pass 80 Release / 79 Lean cases, with independent reliability review.
  The original coverage gaps remain recorded under
  `artifacts/navigator-recovery/final-correctness-gates/`; the new matrix uses
  a separate `final-correctness-gates-v2/` directory. Production code is unchanged
  between those two matrices.

### Immediate Steady-State Regression

- **Current status:** Implementation fixes and comparisons are complete; residual
  cost is an explicit review tradeoff under the user's 2026-09-18 direction,
  not a requirement to recover every percentage point before feature handoff.
  The later five-arm results and all adverse observations remain authoritative;
  neither universal 2–3% bounds nor exact correctness-cost attribution is proven.
- **Earlier occupancy-corrected comparison:** The unchanged original-baseline /
  candidate / original-baseline protocol gives AStar/100 block medians
  157.90 / 156.40 / 154.73 ms, Flow/100 66.47 / 69.91 / 66.54 ms, and
  Flow/500 365.77 / 387.73 / 363.92 ms. Flow remains slower than both controls:
  5.175% / 5.063% and 6.003% / 6.545%, respectively. The correctness fix is
  retained; no favorable-result retry or confirmation was run. All 1,107 raw
  records preserve the literal replay/ray/allocation/GC arrays, with all 70,848
  allocation/GC elements zero. Both 59-file runtimes and 82 source entries match
  before/after. The collector remains paused with identical counters at all six
  boundaries. Evidence: `artifacts/navigator-recovery/regression-controls/occupancy-*`.
  Matching exported histories do not establish identical unexported occupancy work.
- **Attribution control, not a replacement baseline:** The original source can
  skip occupancy updates at the temporary below-ground position. The corrected
  source admits the final position and, after checking the existing ticket,
  resolves the old position to retire any distinct registration. That old-position
  lookup is necessary even when the destination already has a ticket: hosts can
  pre-register destinations, and overlapping grids can resolve the two points
  differently. Independent review rejects skipping it based only on current-prism
  containment. A separate diagnostic build applies exactly the same contact-order
  and ticket corrections to the original baseline. It distinguishes scheduling
  overhead from repaired occupancy work; the untouched-baseline total comparison
  above remains visible and is not superseded.
  The completed corrected-control / candidate / corrected-control comparison
  places every candidate block median between its two corrected controls:
  AStar/100 +1.526% / -2.164%, Flow/100 -0.417% / +0.131%, and
  Flow/500 +0.322% / -0.132%. This does not detect a two-sided scheduling
  regression under equal occupancy behavior. All 1,107 records preserve exact
  replay/ray/allocation/GC arrays, with zero measured allocations/collections;
  corrected 57-file, candidate 59-file and original 59-file runtime manifests,
  and 82/543/485 source manifests, remain unchanged. Collector counters are
  identical. Scratch builds retain one duplicate-analyzer-config warning each;
  deployed dependency DLLs and runtimeconfig files match both earlier runtimes.
  The isolated source and all build-only differences are retained under
  `artifacts/navigator-recovery/corrected-baseline-diagnostic/`; captures use
  `regression-controls/corrected-control-*`. This is attribution, not a silent
  replacement of the original total-cost gate or an unconditional acceptance.
- **Latest cold-arm comparison:** Moving populated adoption-sample handling to a
  private helper, together with the reviewed one-sample deferral corrections,
  leaves AStar/100 block medians 151.98920 / 153.78520 / 153.69720 ms, Flow/100
  66.22595 / 68.61745 / 65.91535 ms, and Flow/500
  363.20655 / 374.87420 / 364.19325 ms. Flow/100 is 3.611% / 4.099% slower and
  Flow/500 is 3.212% / 2.933% slower than both controls. The same 1,107 raw
  records preserve literal replay/ray/allocation/GC arrays; both 59-file frozen
  runtimes and all 80 candidate-source entries match before/after. The paused
  collector's counters remain unchanged. This measures the whole candidate,
  not the helper in isolation. No favorable-result retry or confirmation was
  run. Retain `artifacts/navigator-recovery/regression-controls/cold-*`.
- **Prior entry-gate comparison:** The fixed baseline/candidate/baseline
  protocol gives AStar/100 block medians 153.86970 / 147.05910 / 147.42120 ms,
  Flow/100 66.13220 / 65.60455 / 64.14570 ms, and Flow/500
  354.45110 / 360.24865 / 353.25815 ms. Flow/500 is still 1.636% / 1.979%
  slower than both controls. Smaller-case baseline drift limits conclusions
  about those cases; it does not clear the large-population regression.
  All 1,107 records preserve identical replay/ray histories and zero measured
  allocation/GC arrays; both 59-file runtime manifests and 70 candidate-source
  hashes match before/after. The collector stayed paused with unchanged counters.
  Raw captures and analysis are retained under
  `artifacts/navigator-recovery/regression-controls/entry-*`.
  The candidate also contains the adoption-freshness correctness correction,
  so this does not isolate a causal benefit from the entry gate alone.
- **Rejected lease-slot layout experiment:** A runtime layout diagnostic confirms the Flow cache's
  lease slot grew from 32 to 40 bytes after adding per-lease endpoint proofs.
  Moving reservation-only byte ceilings to one fixed-capacity array restores
  32-byte records, but the unchanged A–B–A protocol still leaves Flow/100
  3.610% / 2.436% and Flow/500 3.777% / 3.089% slower than both controls.
  The experiment adds an array header without reducing total storage and is
  reverted exactly to the preceding source hash. Keep the meaningful new test
  proving a stale reservation alias cannot cancel a newer reservation reusing
  the same slot. Focused Release/Lean suites pass 214/210 tests. All 1,107 raw
  records have exact replay/ray/allocation/GC arrays, with zero measured
  allocations or collections; both 59-file runtimes and 80 source entries match.
  Retain the final control's slow AStar process and the paused collector's two
  15.625 ms CPU-counter increments as environmental caveats, not grounds for
  discarding results or attributing the Flow regression. Evidence remains under
  `artifacts/navigator-recovery/regression-controls/slot-*`.
- **Measured locator and corrective action:** Paired managed thread-sampling
  traces contain 8,368 baseline and 8,412 candidate samples under both the
  instrumented 64-frame block and containing-frame simulation methods. There
  are no lost events, truncated stacks or unresolved leaves. Registration has
  0 / 1,260 inclusive samples, while `CommitFrameMotion` has 1,135 / 1,641.
  This locates work; it does not establish exact call counts or causally explain
  the whole timing gap. The scope includes preflight/warmup/actual/diagnostic
  blocks and excludes acquisition preparation and outside-frame hashing.
  Source shows the contact-order correction admits the corrected ground root,
  exposing redundant same-cell registration attempts previously bypassed by
  the below-prism root. Each duplicate add rolls back and returns failure.
  Check the existing world/occupant/voxel ticket before adding, while retaining
  prior-cell removal and failure ordering. Three RED cases reproduce the false
  warning and stale previous registration, including a full already-registered
  target. Missing-ticket re-registration, a full unregistered target, existing
  duplicate-ID diagnostics and the zero-allocation motion guard remain covered.
  Focused suites pass 70 Release / 68 Lean, with independent source/test review.
  The unchanged unprofiled A–B–A now measures this candidate; no speedup is
  inferred from the trace. Both diagnostic exporters needed an inherited-port
  cleanup after simulation finished; their complete logs, interventions,
  runtime hashes and exact matching replay/ray arrays remain under
  `artifacts/navigator-recovery/steady-cpu-trace/`. The occupancy registry is not
  part of the historical replay hash, so hash equality alone does not establish
  old/new physical-occupancy equivalence.
- **Bounded follow-up profile:** A single 20-second attach-by-PID profile of the
  unchanged corrected candidate finishes normally without inherited diagnostic
  ports or exporter intervention. Its 750 scoped managed samples include
  119 ticket-lookup samples (118 exclusive), versus one resolver sample. No lost,
  truncated or unresolved-leaf samples are reported; all 41 benchmark records
  match prior replay/ray/allocation/GC histories, and runtime/source hashes remain
  unchanged. This narrower window is not directly comparable with the earlier
  8,000-sample captures. It does not establish prism reconstruction as a meaningful
  hotspot, so the proposed extra geometry helper is not implemented. Keep the
  existing world/grid resolution and exact containment rules intact. Evidence:
  `artifacts/navigator-recovery/occupancy-pid-trace/`.
- **Rejected unsafe shortcuts:** Entry-mode-only cleanup bypass and narrower
  cleanup scopes were rejected because host hooks can create new state and
  connector advancement can release ownership later in the heading.
- **Evidence:** The paired final controls below show 2.2–9.0% larger Immediate
  guided-frame block costs. Opting out of scheduling must not pay for its work.
- **Reproduction:** A fixed frozen baseline/candidate/baseline comparison
  reproduces the regression on both sides: AStar/100 block medians
  149.02 / 153.17 / 150.98 ms; Flow/100 63.53 / 66.53 / 63.55 ms;
  Flow/500 346.66 / 367.29 / 349.39 ms. Every capture retains the same
  270 Actual blocks, exact replay/ray histories and zero measured allocation.
  Capture-time variation does not explain away the candidate's added cost.
- **Source findings:** The audit found six additional connector monitor entries
  per ordinary A* agent/frame and unconditional large nullable Flow sample
  cleanup on every heading. Both are now kept off the ordinary Immediate path:
  connector detection first checks existing ownership; a private, lazy reusable
  holder stores the sample only after first Scheduled Flow adoption. Live lease
  validation and `finally` cleanup remain authoritative.
- **Discriminators:** Conditional empty-sample clearing did not resolve the
  slowdown and was reverted. Explicit nullable extraction alone also leaves
  Flow slower. The combined fix passes all 130 focused Release
  acquisition/connector tests and independent source review. Inspection of
  the real prepared Flow/500 fixture found zero additional endpoint proofs among
  all 500 guides, excluding repeated endpoint-stamp traversal in this case.
  The holder introduces one allocation on first Scheduled Flow adoption, not on
  Immediate or every heading. The combined holder/ownership change has block
  medians 147.52 / 65.57 / 356.39 ms (AStar100 / Flow100 / Flow500), versus a
  fresh neighboring baseline of 149.91 / 64.39 / 351.13 ms: still a Flow gap.
  A further focused change skips heading-exit interest cleanup when no A* lease
  exists; ticket cancellation/adoption already releases its interest. It passes
  the same 130 tests and yields 141.94 / 63.73 / 338.79 ms. The confirmation does
  not clear the blocker: baseline 149.33 / 62.94 / 340.55 ms versus candidate
  143.57 / 64.76 / 344.76 ms, leaving Flow 2.88% / 1.24% slower. Independent
  review confirms identical replay/ray histories, zero measured allocations/GC
  and unchanged frozen hashes. These are improvements, not completed
  non-regression verification or proof of each instruction's cost.
- **Further cleanup:** Skip entering A* connector validation when the post-hook
  heading has no A* lease, and fold the one-use private heading helper into its
  existing `try/finally`. Validation-interest bookkeeping is now lazy and reused;
  never-Scheduled steering no longer allocates that optional object. Its latest
  diagnostic medians are 144.28 / 63.24 / 338.16 ms. These do not establish
  non-regression; the final matched comparison below remains unfavorable.
  Independent reviews found no lifetime, virtual-hook or cleanup regression.
- **Final matched follow-up:** A new frozen baseline/candidate/baseline capture
  retains the same three-process protocol and all replay/ray histories. Block
  medians are AStar/100 138.81410 / 141.54750 / 141.60485 ms, Flow/100
  61.02405 / 65.25555 / 61.30010 ms, and Flow/500
  323.27655 / 338.34635 / 327.40910 ms. AStar falls inside its baseline bracket;
  Flow is slower than both baselines: 6.93% / 6.45% for 100 agents and
  4.66% / 3.34% for 500. All measured allocation/GC arrays remain zero. This
  fails acceptance, not just a request for a more favorable capture. Full
  distributions, hashes, raw arrays and paused-collector state are retained
  under `artifacts/navigator-recovery/regression-controls/final-*`.
- **Next structural experiment:** On the same .NET 8.0.29 runtime, allocating
  uninitialized steering objects measures 616 bytes/object on develop, 912 in
  the initial feature snapshot, and 688 after the current fixes. This isolates
  object footprint, not lifetime memory or a causal timing attribution. Move
  optional acquisition/connector bookkeeping into one private lazy reusable
  state object; keep mode/status on steering and the large Flow sample separately
  lazy. Ordinary Immediate use and failed-load validation must not create it.
  Reentrant hooks, unconditional exit cleanup and registered-interest identity
  remain unchanged. Measure both Immediate and Scheduled first-use costs before
  deciding whether to retain the experiment. The implemented grouping reduces
  the steering object to 632 bytes and passes 297 focused Release tests. A new
  public-behavior test caught and fixes a refactor boundary: restoring Scheduled
  intent, then switching to Immediate before its first heading must defer this
  frame and acquire the next. Its fresh frozen baseline/candidate/baseline block
  medians are AStar/100 144.05510 / 143.18810 / 140.55740 ms, Flow/100
  61.82025 / 64.17270 / 61.80250 ms, and Flow/500
  329.46805 / 339.57935 / 328.62080 ms. Flow remains slower than both controls
  (3.81% / 3.84% and 3.07% / 3.33%). All replay/ray histories and zero measured
  allocation/GC controls match. Source review finds no actionable issue; this
  still fails performance acceptance.
- **Heading-body discriminator:** One explicitly diagnostic-only frozen build
  substitutes develop's original `GetHeading` body into the optional-state
  candidate, leaving other methods unchanged. It is unsafe for Scheduled mode
  and will not ship. The production source was restored byte-for-byte before
  running the unchanged default-Immediate fixture. The result can distinguish
  this whole-body/code-generation effect from the rest of the branch; it cannot
  justify deleting required exception/reentrant cleanup. Its block medians are
  140.05010 / 62.78785 / 331.75060 ms: a partial gap reduction, not a completed
  fix. A subsequent product experiment kept a thin public `try/finally` around a
  private heading calculation and gates optional entry bookkeeping on existing
  state or Scheduled mode. All 297 focused tests pass; independent review
  confirmed cleanup and restored-Scheduled first use remained intact. Its fresh
  matched before/candidate/after block medians were AStar/100
  141.32170 / 149.58110 / 142.57110 ms, Flow/100
  62.09255 / 65.68190 / 63.08875 ms, and Flow/500
  328.34285 / 353.46255 / 339.35940 ms. It was slower than both controls in all
  cases. Both the wrapper and entry gate were reverted; all 68 retained
  source/test hashes match the optional-state snapshot. Required cleanup remains
  in the original heading body; no private `GetHeadingCore` is retained.
- **State-grouping verification:** Release passes 2,997 core + 65 adapter tests;
  ReleaseLean passes 2,919 + 65. Both configurations build both library target
  frameworks without warnings. Core coverage reaches 32,053/32,053 lines and
  3,154/3,154 methods, but only 13,112/13,116 branches. Follow-up tests must cover
  first-use and mode-change behavior before this verification gate is complete.
  The unchanged allocation guard passes these runs; its earlier failures remain
  unexplained and blocking.
- **Final behavior verification:** Three additional cases cover restored
  Scheduled-to-Immediate A* guidance, protected Scheduled validation before any
  heading, and fresh Immediate connector adoption against a closed scheduler.
  Independent test review finds no actionable issue; 300 focused Release tests
  pass. The full normal matrix is **red**: Release 2,999/3,000 core tests and
  ReleaseLean 2,921/2,922; both adapter suites pass 65/65. Separate core coverage
  runs pass all 3,000 and 2,922 tests respectively, covering exactly
  32,053/32,053 lines, 13,116/13,116 branches and 3,154/3,154 methods, including
  fully covered methods. Adapter coverage is 186/186 lines and 28/28 branches.
  Both library target frameworks build without warnings in both configurations.
  These are Windows results; no new Linux/DocFX validation is claimed. Runtime
  source is unchanged from the retained measured optional-state snapshot.
- **Full verification before state grouping:** Release passes 2,996 core + 65 adapter tests;
  ReleaseLean passes 2,918 + 65. Both configurations build both library target
  frameworks without warnings. Core coverage is exactly 32,036/32,036 lines,
  13,080/13,080 branches and 3,154/3,154 methods (including fully covered
  methods); adapter coverage remains 186/186 lines and 28/28 branches. The
  cleanup removes a now-unreachable private no-lease branch and extends the
  existing reused-guide test to reject a disposed connector generation.
  This passing matrix does not fix or supersede the allocation recurrence.
- **First-use accounting before state grouping:** Existing Flow/500 Scheduled B smokes retain the
  same readiness and replay arrays for all five episodes, twice each. Measured
  first-adoption episode allocation rises by 156,000 bytes (312 per agent),
  including interest creation moved out of untimed setup and the sample holder.
  This is not net additional lifetime memory or per-heading allocation. Warm
  acquisition measures 443,280 bytes versus 287,280 before these fixes; the
  first-use tradeoff is explicit, not hidden in steady-state measurements.
- **State-grouping first-use control:** Existing Scheduled B smokes compare
  the pre-grouping and retained runtimes for Flow/500 and AStar/100, two repeats
  of each of five episodes (40 records total). All agents become ready with
  identical readiness and replay arrays, no failed/cancelled/censored requests.
  Flow completion remains 57/16/59/59/16 frames; AStar remains
  361/6/361/361/361. All ten Flow allocation totals match exactly. Eight of ten
  AStar totals match; the pre-grouping first cold record has 3,168 extra bytes
  and its second restore record has 5,096 extra bytes, without an identified
  allocator. Preserve those differences; they are not evidence of an allocator
  fix. The fixture explicitly configures Scheduled mode before timing, including
  on restored shells, so state creation is outside these counters. These smokes
  establish behavior and measured episode costs only, not net lifetime memory
  savings or timing acceptance. Both runtime manifests remain unchanged.
  Independent raw-log review confirms all 40 summary rows and paired work,
  outcome and readiness histories. GC timing and untimed restore-preparation
  allocations differ; neither is claimed identical or attributed to a fix.
- **Completion:** Remove the responsible added work without weakening currentness,
  moving-origin or mode-switch behavior; verify with matched measurements,
  behavioral tests and independent review. Preserve all unfavorable controls.

### Zero-Allocation Gate Regression

- **Status:** Measurement correction verified by fresh Windows/Linux suites,
  exact coverage and documentation checks in v3 and v4. V4 additionally verifies
  the five retained public occupancy cases and fresh Scheduled semantic smokes. The earlier
  Linux Lean no-GC completion failure and its diagnostic evidence remain below;
  the selected-volume failure has a directly captured cause, not a blanket
  attribution of every historical failure. No zero threshold, measured loop,
  warmup or exclusion was relaxed, and no retry-until-green was used.
- **Observed failures:** Task5 full Release first reported 6,008 bytes in the
  selected-volume Flow sample guard, then 5,344 bytes in the volume endpoint
  evaluation/reset guard. Task6 Lean later reported 5,144 bytes in that endpoint
  guard. The final Release gate reported **3,064 bytes**, passing 2,995/2,996 core
  tests and 65/65 adapter tests. The first failure's original binaries were not
  preserved; the later failed snapshots and their provenance limits are retained.
- **Evidence:** `artifacts/navigator-recovery/task6-final-release-allocation-recurrence/`
  contains the final failed binaries, log and pre-build source hashes. Earlier
  snapshots are in `task6-lean-allocation-recurrence/`; full diagnostic history,
  commands and logs remain in the ignored `navigationRecoveryPlan` task workspace.
- **Investigation so far:** Isolated/class-set checks and later full runs passed,
  but did not establish a fix. Sampled/per-object profiler attempts did not
  attribute the measured bytes; one profiled run instead failed the separate
  motion-commit guard with 16,384 bytes. A temporary stack-only phase diagnostic
  retained the original two warmups/256 iterations/zero guard and measured zero
  in every phase. It was restored exactly. None supersedes the failed gate.
- **Current boundary:** Frozen baseline/candidate GridForge, FixedMathSharp and
  SwiftCollections runtime binaries match. Check branch implementation and
  test-history changes instead of assuming dependency drift or a runtime defect.
- **History control:** Seventeen new cases from three endpoint-proof methods
  precede the unchanged volume guard. A proposed two-arm history contrast used
  different copied assembly paths; xUnit hashes that path into test identities,
  changing the shared execution order. Those passing runs are invalid for causal
  comparison. The corrected same-path pair passed 1,553/1,553 and 1,536/1,536,
  with every shared test ID and relative order matching. Both unchanged
  allocation guards passed: the experiment is inconclusive, not a fix.
  The guard and frozen failed runtime remain unchanged.
- **Follow-up verification:** Full Release (2,996 core + 65 adapter) and
  ReleaseLean (2,918 + 65) pass after the initial Immediate-path fixes. Those
  changes do not identify the endpoint guard's allocator, so the failure remains
  open. Native watching of the allocation counter's underlying fields was the
  next diagnostic. The user subsequently installed Windows Debugging Tools;
  CDB 10.0.28000.2705 and matching .NET 8.0.29 native symbols were verified.
- **Latest recurrence:** The follow-up Release core coverage run reports
  **4,696 bytes** in the same unchanged volume endpoint guard (2,995/2,996 pass).
  It used Coverlet; no attribution follows from that fact. Its log, TRX, source
  hashes and restored original binaries are retained in
  `artifacts/navigator-recovery/immediate-fix-coverage-probe/`. Instrumented
  binaries were restored by the collector, not retained as a failing-runtime
  snapshot. This fresh failure remains part of the blocker.
- **Final retained-source recurrence:** After adding three first-use/mode-change
  behavior cases (runtime source unchanged), the full normal Release run reports
  **6,008 bytes** in `FlowGuide_ShouldSampleTheExactSelectedVolumeShortcut`.
  It passes 2,999/3,000 core tests and 65/65 adapter tests. The original test
  binaries were copied and hashed before execution; the log, TRX and current
  source hashes are retained under
  `artifacts/navigator-recovery/optional-state-behavior-final/Release/`.
  This reproduces the earlier selected-volume failure rather than clearing the
  allocation gate. The full normal ReleaseLean run separately reports
  **2,200 bytes** in
  `CommitFrameMotion_ShouldRefreshMovingContactAndCommittedCellWithoutAllocating`,
  passing 2,921/2,922 core tests and 65/65 adapter tests. Its original binaries
  were likewise copied and hashed before execution under the sibling
  `ReleaseLean/` directory. Both complete failing test bodies match develop;
  warmups, measured loops and zero-byte assertions were not changed. Separate
  coverage runs pass, but do not identify the allocator or supersede either
  normal-run failure. No further reruns are used to replace these red gates.
- **Native capture, September 17:** Replaying all 158 preserved Release runtime
  files at the original test path reproduced the same **6,008-byte** failure
  (2,999/3,000 core tests, 65/65 adapter). Four hardware write watchpoints covered
  the allocation-context inputs between the existing two counter reads and
  around the original 64-sample loop. The thread had exactly 6,008 bytes of
  unused allocation space before sampling. Only two writes occurred: the native
  background-GC thread called `WKS::void_allocation`, clearing `alloc_ptr` and
  `alloc_limit` while leaving `alloc_bytes` and `alloc_bytes_uoh` unchanged.
  The original test thread's second counter read therefore increased by exactly
  6,008 bytes, without a managed allocation in that interval. Independent review
  verified the complete interval, arithmetic and matching runtime implementation.
  Full-suite case membership and all 3,000 test identities match, but execution
  order differs (the guard moved from index 2,569 to 2,250); no controlled
  test-history contrast is claimed. The observed counter writes prove this
  captured failure directly.
  This corrects the earlier overly broad rejection of allocation-tail accounting:
  the ordinary compensated retirement path differs from background-GC voiding.
  Debugger pauses change timing, so this is causal counter evidence, not a
  performance measurement. The replay log, disassembly, thread/counter fields,
  watchpoint transcript, full test result and binary manifests are retained in
  `artifacts/navigator-recovery/native-debug/replay-1/`; the original failing
  binaries remain unchanged and the current build output was restored afterward.
- **Focused correction:** The allocation helper now protects synchronous measurement with
  an explicit, bounded no-GC region after its existing warmups. Keep both counter
  reads, the measured work and the exact zero assertion. Failure to start or
  successfully end the region must fail the test; never fall back, retry, subtract
  an assumed tail or tolerate bytes. Positive-allocation and cleanup controls
  must prove that real allocations still register and an induced collection
  invalidates the measurement. This changes the measurement setup, not library
  behavior or process-wide runtime configuration. The endpoint and motion
  failures are not individually attributed by the Flow capture: each still
  needs to pass its original zero threshold and behavioral checks.
- **Oracle controls:** Five behavior tests cover zero work, genuine retained
  allocation with exactly one action invocation, induced collection, exception
  cleanup and rejected nesting. The original helper fails two controls. Initial
  no-GC admission exposed an additional .NET 8.0.29 ownership hazard: a failed
  nested admission clears the existing region's state. Checking latency mode
  before admission preserves the outer owner. Final focused runs pass 649
  Release and 635 ReleaseLean cases, including every existing helper-caller
  class, the three converted guards and all steering matches. These results
  are retained in task output, not separate TRX artifacts; they are not a full
  matrix or timing result. Independent source/test review found no actionable
  issue. The 16 MiB process-wide headroom is not an allocation allowance, and
  region admission or end failure is never suppressed.
- **Completion:** Verify the corrected allocation oracle and full required
  matrix, retaining every historical red result and the cause's scope. This work
  remains part of this plan, alongside the separate Immediate performance gate.

### Corrected-Source Normal Matrix

The full normal solution suites pass once each on the final adoption-deferral
and shared-oracle source: **3,012 core + 65 adapter** in Release, **2,934 + 65**
in ReleaseLean, with zero failures or skips. Both solution builds include
`netstandard2.1` and `net8.0`, with zero warnings/errors. All 158 core and 164
adapter output hashes in each configuration match their pre-test manifests.
No retries, counter subtraction, tolerance increases or core collector were
used. Logs, TRX and manifests are retained under
`artifacts/navigator-recovery/oracle-adoption-normal-final/`.

All 30 other synchronous allocation intervals now use the same corrected
measurement helper. Their warmups, operations, iteration counts and assertions
are unchanged, including three existing `<512` checks and two positive-allocation
accounting ranges. The per-frame heading test still excludes `Context.Simulate()`.
Only the helper reads the current-thread counter directly. Independent review
found no scope or assertion changes. This verifies the measurement fix without
attributing every historical failure to the captured Flow cause.

A subsequent fresh matrix, including the stale-reservation reuse regression,
passes **3,013 core + 65 adapter** in Release and **2,935 + 65** in ReleaseLean.
The separately collected core coverage suites pass all 3,013/2,935 cases.
Both configurations cover exactly **32,075/32,075 core lines,
13,132/13,132 branches, and 3,155/3,155 covered and fully covered methods**;
adapter coverage is **186/186 lines, 28/28 branches, and 11/11 covered and fully
covered methods**. Both target frameworks build with zero warnings/errors,
all six TRX files have zero failed/not-executed tests, and all output manifests
remain unchanged. Evidence: `artifacts/navigator-recovery/oracle-adoption-gates-current/`.
Those gates predate the occupancy ticket correction and connector-hook tests.
The final-source Windows refresh is retained separately under
`artifacts/navigator-recovery/final-correctness-gates-v2/`:

| Configuration | Normal core / adapter | Explicit core coverage tests | Core lines | Core branches | Core covered / fully covered methods |
| --- | ---: | ---: | ---: | ---: | ---: |
| Release | 3,027 / 65 | 3,027 | 32,104 / 32,104 | 13,160 / 13,160 | 3,158 / 3,158 |
| ReleaseLean | 2,949 / 65 | 2,949 | 32,104 / 32,104 | 13,160 / 13,160 | 3,158 / 3,158 |

Each method count is against 3,158 total methods. The adapter remains exact in
both configurations: 186/186 lines, 28/28 branches and 11/11 covered and fully
covered methods. Both solution builds include `netstandard2.1` and `net8.0` and
report zero warnings/errors. All normal and explicit-collector suites pass with
zero failures/skips. No runtime safety branch or coverage exclusion was removed
to close the three gaps from the preceding matrix. Fresh Linux/documentation
checks and final-source Immediate timing are still required.

### Final Linux No-GC Measurement Failure

The same final portable outputs pass all **3,027 Release core** tests and both
**65-test adapter** suites on Ubuntu / .NET 8.0.29. Lean core passes **2,948/2,949**:
`ComputeCombinedSteering_ShouldNotGrowForOccupantsOutsideUngroupedAvoidance`
fails in `GC.EndNoGCRegion()` with `NoGCRegion mode must be set.` The strict
helper rejects the measurement, so this is not a passing allocation gate and no
measured-byte result can be inferred. Its 16 MiB headroom, measured operation,
warmups and exact zero assertion are unchanged. No acceptance retry was run.

The original copied runtime, XML, log, failure summary and before/after hashes
remain under `final-correctness-gates-v2/linux/ReleaseLean/`. All 482 source inputs,
38 authored documentation inputs and Windows/Linux output hashes remain unchanged
through that gate. DocFX was already running when the failure was identified and
completed with zero warnings/errors; site-link/wiki/example checks and timing
captures are held.

The matching runtime source clears its no-GC state when a collection interrupts
the region, so the exception alone does not distinguish allocation pressure,
an induced collection or admission-state interference. Investigation uses a
separate diagnostic build recording scalar timestamps, latency mode, collection
counts and byte counters around the unchanged measured action. Its temporary
helper logging is already removed from the working source, verified against the
original hash. Diagnostic outputs and the runtime source snapshot are under
`artifacts/navigator-recovery/nogc-linux-diagnosis/`. This evidence gathering must
not replace the failed gate, increase allowances, suppress region errors or retry
until green.

One instrumented Linux launch passed all 2,949 tests and did not reproduce the
failure. Its 51,341-event trace lost no events; the original failing action
measured zero bytes with no collection in its interval. This is a non-reproduction,
not a replacement for the failed uninstrumented gate. The diagnostic report and
raw trace remain in `nogc-linux-diagnosis/gc-diagnostic-01/`.

A separate standalone .NET 8.0.29 probe, with no Trailblazer dependency, reproduced
`TryStartNoGCRegion(16 MiB, false)` returning `true` while its immediate latency
mode was `Interactive`; `EndNoGCRegion` then threw the same exception. The probe
stopped at its first anomaly, on the second attempt. One bounded worker allocation
completed during admission and none afterward. This establishes a runtime
admission failure, but does not prove that exact interleaving caused the earlier
suite failure. Evidence: `artifacts/navigator-recovery/no-gc-admission-probe/`.

The runtime implementation waits an existing background collection **after**
preparing no-GC state. A single-variable probe moves that wait before admission
using a forced blocking full collection: all 64 attempts then entered an active
region and ended successfully in one retained launch. Independent source review
supports that preparation, plus an active-mode check before the baseline/action;
new unrelated activity can still invalidate a region, so strict End validation
remains required. The test helper now follows that ordering without changing its
16 MiB headroom, counters, actions, warmups or assertions. It does not retry,
subtract bytes, suppress errors or affect benchmark/runtime settings. The existing
controls still test real allocations, exactly-once work, induced-collection
rejection, exception cleanup and nesting; successful work also observes active
mode. The original failed gate and both bounded probes remain retained.

The corrected helper passes all five focused controls in both configurations,
then a fresh full matrix under `artifacts/navigator-recovery/final-correctness-gates-v3/`:
Windows normal and explicit core suites pass 3,027 Release / 2,949 Lean tests;
both adapter suites pass 65 tests. Copied portable outputs pass the same four
suites on Linux / .NET 8.0.29. Both core reports retain exact coverage of
32,104 lines, 13,160 branches and 3,158 covered and fully covered methods; adapter
reports retain 186 lines, 28 branches and 11 covered/fully covered methods.
Both target frameworks build with zero warnings/errors. Fresh DocFX, workflow
assets/content, local links across 135 HTML pages, four wiki tests and the runnable
example all pass. All 482 source/build/test inputs, 38 authored inputs and
Windows/Linux output manifests remain unchanged through verification. No failed
executed gate was retried. This closes the allocation-measurement blocker for
the verified source, not the separate timing gate or every historical failure.

### Final Timing Stopped Before Candidate Measurement

The approved final sequence is original baseline / final candidate / original
baseline, followed by the same candidate bracketed by equal-correctness controls.
The freshly frozen candidate's core and six shared dependency DLLs match the
passed v3 Release outputs. During the first baseline arm, processes one and two
completed; process three threw `IndexOutOfRangeException` in
`GridTracer.SortIntervals` during AStar/100 setup, before any A* measured sample.
BenchmarkDotNet continued its two Flow cases, but the wrapper rejected the
incomplete arm. No candidate, subsequent control, retry or Scheduled smoke ran.

Raw logs and immutable manifests are retained as
`artifacts/navigator-recovery/regression-controls/final-original-before-*`.
No performance summary was accepted from that partial corpus. The reported
source line is an optimized method-entry sequence point, not proof of which
index failed. Source review establishes safe sorter bounds for valid captured
array/count values and finds preallocated, query-owned workspace storage, but
has not established the exception's cause. No sorter, workload or runtime setting
is changed to make the failure disappear. The exception's occurrence in the frozen baseline
does not by itself attribute a defect to GridForge or absolve caller ownership.

The installed Windows debugger enabled a finite first-chance capture batch on
that same frozen baseline: 12 fresh processes, each running the original three
cases with 10 warmup and 30 measured blocks per case. All 36 cases completed
without a target exception or native range-check-helper stop in 12 minutes
49 seconds. Every run verified the exception filter and native breakpoint;
all 59 runtime files and 482 source/build/test inputs remained unchanged. No
fault-state dump was available. Evidence is retained under
`artifacts/navigator-recovery/sortintervals-native-batch-1/`; its diagnostic
timings are not acceptance evidence. Non-reproduction does not close the failure.

After independent methodological review, a separate six-arm timing experiment
is authorized on the unchanged frozen inputs: original / candidate / original,
then equal-correctness control / the same candidate / equal-correctness control.
Each arm retains the existing three-process protocol and all observations.
The first setup, runtime, protocol or integrity failure stops the experiment;
no replacement process or selective rerun is permitted. The two comparisons
remain separate, and corrected-control parity cannot waive original total cost.
This experiment can measure final-source performance independently of the
currently unrepeatable exception; it cannot establish a crash fix or completion.

All six arms completed with the frozen candidate and unchanged protocol.
Actual complete 64-frame block medians in milliseconds were:

| Case | Original before | Candidate | Original after | Candidate change before / after |
| --- | ---: | ---: | ---: | ---: |
| AStar/100 | 141.53500 | 143.82675 | 141.80695 | +1.619% / +1.424% |
| Flow/100 | 62.64375 | 64.82565 | 62.72715 | +3.483% / +3.345% |
| Flow/500 | 329.21425 | 345.55245 | 334.69875 | +4.963% / +3.243% |

The original total-cost gate remains failed. In the separate equal-correctness
bracket, AStar is 0.157% / 1.061% faster, while Flow/100 is +1.323% / -0.074%
and Flow/500 is +2.383% / -0.147%. Both Flow medians lie between their controls;
this is no two-sided residual signal, not proof of zero scheduling overhead.
Do not pool candidate arms or subtract separate brackets to claim exact causes.
All 2,214 records retain identical exported replay/ray/allocation/GC histories,
with zero measured frame allocation/GC elements. Runtime and source manifests
are unchanged through the final snapshot. Full distributions, process medians
and environment records remain under `regression-controls/final-independent-*`.
The broader BenchmarkDotNet allocation scope is not zero: all 36 Flow case/process
rows report bytes (34 at 1,296 B, one at 624 B and one at 960 B); AStar rows display
`-`. These values remain separate from the exported frame counters and are not
attributed to a particular allocator or the previously diagnosed GC behavior.
These results precede the occupancy-ticket experiment below.

### Live-Validated Occupancy Ticket Experiment — Rejected

The profile's repeated occupancy-registry lookup motivated a bounded experiment:
retain only a generation-stamped ticket on the Navigator and validate it against
the current resolved voxel's live occupant bucket and exact owner each time.
On miss, keep the existing registry lookup and registration fallback. Keep
previous-cell resolution and retirement even for preregistered destinations;
clear the unrecorded hint on reset and successful load. Do not cache geometry,
grid objects or an occupancy boolean, or add upstream APIs/event subscriptions.

Independent source review found this feasible within the existing single-owner
world-lifecycle boundary; it does not establish concurrent grid teardown safety
or a speedup. Characterize external removal/re-add, slot reuse and completed
grid replacement before changing runtime logic, then measure the candidate.
Discard the hint if the benefit does not justify its additional 16-byte value.

Test-first characterization passes nine occupancy cases on the pre-hint runtime,
including another owner reusing a removed ticket's slot, same-owner re-registration
and completed grid replacement. The candidate adds wrong-owner hint and add-callback
repair checks. Independent review then found a candidate-specific reset ordering
bug: a synchronous removal callback could repopulate the hint before reuse under
a different identity. A public registry assertion reproduces it RED; clearing the
hint after deregistration callbacks makes that exact test GREEN. Load already
clears after deregistration. The reset test also verifies callback completion
outside the handler so swallowed callback exceptions cannot hide broken setup.
The small production change passed independent source review and focused checks
(336 Release / 289 Lean); those checks did not certify the full feature matrix.

The fixed experiment used original-before, parent-before, ticket candidate,
parent-after and original-after, three fresh processes per arm. All 1,845 records
completed without a setup/protocol failure. Actual complete 64-frame block medians
produced these differences; positive means slower:

| Case | Ticket vs parent before / after | Ticket vs original before / after |
| --- | ---: | ---: |
| AStar/100 | +2.371% / -0.709% | +3.688% / +2.584% |
| Flow/100 | -0.658% / +0.228% | +2.769% / +3.856% |
| Flow/500 | -0.898% / -0.056% | +2.582% / +3.254% |

Only Flow/500 decreases against both parent controls, with a 0.056% later-control
margin against 0.843% control drift. This does not justify 16 bytes per Navigator
and additional lifecycle state. The total-original gate still fails. The hint
was removed from all three runtime files, restoring the measured parent source.
Five additional public behavior cases remain; the one internal hint-input test
was removed with the implementation it exercised. No upstream code changed.

All raw replay/ray arrays match and measured frame allocation/GC arrays are zero.
Broader BDN diagnostics remain separate: 28 Flow case/process rows report 1,296 B,
two report 960 B; all 15 AStar rows report 0 B. The paused collector accumulated
15.625 ms kernel CPU during the candidate arm; no result was corrected or waived
for that activity. Frozen runtimes, source and authored inputs remained unchanged
through the post-capture snapshot; subsequent rollback is not covered by that
snapshot. Fresh v4 validation of the restored runtime and retained tests passes:
3,032 Release / 2,954 Lean core tests and 65 adapter tests per configuration on
Windows and copied portable Linux execution, with exact unchanged coverage and
green documentation checks. The two Scheduled AStar/100 and Flow/500 semantic
smokes each pass five scenarios and their replays without failed/cancelled/censored
requests. An interrupted first smoke setup has no accepted result and is retained
separately. All runtime/source identities are checked; this is not timing acceptance.

Evidence is under `ticket-hint-characterization/`, `ticket-hint-focused/` and
`regression-controls/ticket-hint-*` in `artifacts/navigator-recovery/`.

### Boundary Review After The Rejected Hint

Independent source review found no smaller safe deletion in the remaining
occupancy/Flow checks. Current and previous occupancy resolution preserve
overlapping-grid selection and previous-cell retirement. Final committed-cell
resolution occurs after virtual occupancy handling, synchronous occupant callbacks
and motor host callbacks, so it cannot blindly reuse the earlier result.

Flow status validation decides retry/admission before virtual hooks; sampling
validates after those hooks and direct-path refresh; its closing validation catches
cache invalidation/reset during sampling. The guide lock does not exclude those
cache mutations. Unstamped Solid payloads still need the separate pre/post raw-world
epoch reads. Removing any of those checks would change freshness or observable
hook/retry ordering, not merely remove duplicate work. No such shortcut was made.
A combined retained-guide validation/sampling boundary would be an architectural
change requiring an explicit hook-order contract and new regression evidence;
this review is not proof that such a design will outperform the current one.

### Retained Flow Entry Experiment — Rejected

This experiment preserved those validation boundaries but avoided looking
up a payload that the guide already owns. An ActiveGuide slot cannot be recycled
until its guide lock has detached it. Its cache entry is never rebound or pooled;
eviction detaches the same immutable payload, while reset/disposal/exact removal
invalidate it. Capture that exact entry during conversion, retain it under the
guide lock and clear it on detach. Public guide generations and the cold post-Bind
slot/generation check remain unchanged. This is not a memoized freshness result.

A naive per-entry volatile read would expose a partly completed multi-entry reset.
Independent review caught that change before implementation. Preserve the sweep
boundary with a cache-wide volatile flag around lock-held bulk invalidation. Read
flag, one-way entry invalidity, then flag again; if either flag read is true, take
the existing cache lock before deciding entry validity. A completed bool cycle
does not revive a never-rebound, permanently invalid entry. All writes and reads
must retain their acquire/release ordering, and current sweeps must not nest.
This proof uses the [C# volatile ordering contract](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#1554-volatile-fields),
not an assumption that volatile provides mutual exclusion.

Characterize promotion, exact entry identity, interrupted binding, stale aliases
and sampling-time invalidation before changing production. Preserve every graph,
endpoint-proof and raw-world check, and unchanged query budgets/outputs. Replace
the saved payload-generation field with the entry reference only where the
guide-lock ownership proof makes the repeated slot lookup unnecessary. Actual
layout/allocation gates still determine the footprint; equal field widths alone
are not a size measurement. No upstream APIs, public contract or hook ordering
change is part of this experiment. Keep it only after independent implementation
review, focused/full checks and a fresh unchanged-harness matched comparison.

Characterizations passed on unchanged production (110 Release cases). The reviewed
candidate passed 610 Release and 605 Lean focused cases, including two controlled
partial-sweep cases and unchanged allocation guards. Separate .NET 8.0.29 x64
footprint probes measured identical shallow sizes: guide 152 bytes, cache 216
bytes and entry 32 bytes. These are not whole-scene memory measurements.

The fixed five-arm comparison completed all 15 fresh processes, with 1,845
records and 86,400 Actual frames. Relative to the parent before/after controls,
Flow/100 improved 0.858%/1.320%, but Flow/500 changed -0.709%/+0.217% and did not
beat both controls. Both Flow cases remained slower than both original controls:
Flow/100 +2.554%/+3.409%, Flow/500 +2.131%/+4.803%. The small 100-agent gain does
not justify the added invalidation protocol without a two-sided 500-agent gain.
All literal replay/ray arrays matched and measured frame allocation/GC arrays
were zero; broader BenchmarkDotNet allocation reports are retained separately.
The unusually slow original-after AStar process (208.62115 ms median versus
141.99430/143.04365 ms for its neighbors) remains included without attribution or
replacement. Broader BDN results include nonzero 960/1,008/1,296-byte rows, and
the paused collector accrued 15.625 ms kernel time during parent-before; neither
observation is subtracted or used to dismiss the measurements.

Restore the two Flow production files byte-for-byte to the frozen parent and
remove the standalone experimental entry type. Retain the seven portable
characterization cases, but remove the two tests specific to the rejected
private sweep flag. The frozen candidate, exact provenance, footprint results,
raw captures and comparison remain under ignored `artifacts/navigator-recovery`
and `.superpowers/sdd/navigationRecoveryPlan`. Full v5 validation was prepared,
not executed or claimed for this rejected candidate. The earlier native interval
sort failure remains unresolved; these clean launches do not establish a fix.

### Same-Root A* Dependency Walk — Retained

An A* guide currently walks its leased graph's dependencies, checks the world,
then walks the live graph's dependencies again. When both graph references are
identical, the second walk repeats a pure predicate over the same immutable
state. Keep the initial full walk, both world checks in their existing order,
the live `store.Current` read, endpoint-proof validation and lease accounting.
Skip only the second walk when the live root is reference-identical; a different
root still receives full validation. No retained state or new public API is needed.

Do not remove the first world check: holding `GridWorld.ChangeSyncRoot` can pause
it while an incompatible graph is published without changing the raw-world
sequence. Moving the only world check after the live graph read could miss that
publication. Add real public-guide compatible/incompatible publication
characterizations at this blocking boundary before implementing the narrower
same-root change. Independent source review supports the immutable-root proof;
test, coverage and fresh timing evidence must still determine acceptance.

The two publication cases passed with the retained Flow characterizations on
unchanged production (117 Release cases). The applied conditional has independent
source/test review approval and passes 615 Release / 610 Lean focused cases,
including all A* concurrency tests and unchanged allocation guards. It adds no
fields, helper types or public API.

The predeclared five-arm comparison completed with these Actual 64-frame block
medians (milliseconds):

| Case | Original before | Parent before | Candidate | Parent after | Original after |
| --- | ---: | ---: | ---: | ---: | ---: |
| AStar/100 | 141.17985 | 144.26580 | 110.15715 | 143.38175 | 140.00830 |
| Flow/100 | 62.42650 | 65.06720 | 65.15625 | 64.96425 | 62.01040 |
| Flow/500 | 332.98025 | 346.03930 | 343.28035 | 344.28025 | 324.71155 |

**Keep:** AStar improves 23.643% / 23.172% against the two parent controls and
21.974% / 21.321% against the two originals. Only 38/5,760 AStar Actual frames
exceed 2 ms, versus all 5,760 in every control. All 15 fresh processes completed;
1,845 records and their literal checksum/ray/allocation/GC arrays match. All
86,400 Actual frames remain in the distributions, with none exceeding 31.25 ms.
Collector identity, CPU and I/O stayed unchanged. Source/runtime hashes and
inventory checks passed before CPU release. Only the core DLL/PDB changed;
benchmark and upstream binaries match the parent.

This does **not** clear Flow's original total-cost regression: Flow/100 remains
4.373% / 5.073% slower and Flow/500 3.093% / 5.719% slower. The AStar-only change
does not establish a Flow optimization. Narrow frame allocation arrays are zero,
but broader BDN reports retain 28 Flow rows at 1,296 B and two at 960 B; the
15 AStar rows report zero. No observations were removed or replaced. Exact
process medians, tails, warmup/diagnostic trends, hashes and raw logs remain in
the ignored `astar-same-root-*` evidence. Fresh full validation is still required,
and successful launches do not explain the original interval-sort exception.

### Lazy Group-Steering Reciprocal — Rejected

`ComputeCombinedSteering` divides by the group radius before discovering whether
any group member contributes. Defer that pure fixed-point reciprocal until the
first actual contribution, using the same captured radius. Keep scan order,
getter calls, membership checks, signed-radius behavior and all force arithmetic.
Do not gate on initial group membership: host getters can change it during the
scan. A zero radius cannot pass the existing positive-distance/radius-squared
contribution condition. Independent source review supports this narrow change;
it needs no fields, retained state or API.

The existing separation test was strengthened with two contributing neighbors
and literal exact force expectations for positive and negative group radii.
Both cases passed before production changed; nullable annotations introduced by
the test edit were corrected without removing the original warning logs. The
reviewed candidate passed 662 Release / 657 Lean focused cases without warnings,
errors, failures or skips. It then completed the fixed five-arm comparison:

| Case | Original before | AStar parent before | Candidate | AStar parent after | Original after |
| --- | ---: | ---: | ---: | ---: | ---: |
| AStar/100 | 141.65580 | 110.99355 | 110.71355 | 108.65825 | 142.41620 |
| Flow/100 | 61.10590 | 65.24445 | 65.81945 | 65.31575 | 63.03655 |
| Flow/500 | 326.28885 | 347.75075 | 348.85195 | 342.96780 | 338.33635 |

Actual 64-frame block medians are milliseconds. **Reject:** Flow/100 costs
0.881% / 0.771% more than the two AStar-parent controls; Flow/500 costs
0.317% / 1.716% more. AStar lies between its parent controls. Removing an unused
division did not establish an end-to-end improvement. Restore the original
calculation; retain the stronger behavior tests. The restored Groups file matches
the retained AStar source byte-for-byte.

All 15 processes and 1,845 records completed, with exact literal replay/ray/
allocation/GC arrays and final source/runtime inventory checks passing before
any subsequent edits. No Actual frame exceeded 31.25 ms. Broader BDN reports
retain 28 Flow rows at 1,296 B and two at 960 B; all 15 AStar rows report zero.
The paused collector accrued 15.625 ms user CPU during original-before and
15.625 ms kernel CPU during original-after; its identity and I/O were unchanged.
These observations were neither subtracted nor used to excuse the result.
All raw evidence remains under the ignored `steering-lazy-inverse-*` paths.

### Sparse Flow Edge Replay — Confirmed Correctness Finding

Reviewing duplicate prism calculations exposed a different problem: acquisition
assigned selected-edge ordinals using physical surface edges, but sampling
replayed structural edges, which also include authored cells with no physical
voxel. In a public three-cell authored sparse map with only cells 1 and 2 physically
present, a Flow request from cell 1 to cell 2 succeeded, then returned a successful
**leftward** sample instead of the required rightward heading. The new public
regression reproduces this exact failure; no synthetic payload or graph was used.

The sampler and the relevant enumeration choices are unchanged from `develop`;
this source evidence identifies an inherited mismatch, not scheduling overhead.
The new regression has not been executed against the separately frozen original
binary. Keep that distinction explicit.

The initial sampler-only correction passed 706 Release / 701 Lean cases, but
further review found it incomplete. A second public regression authors cells
0–65, initially materializes only cells 64 and 65, and acquires Flow from 64 to
65. Materializing cell 63 then changes physical ordinal zero: the retained guide
still reports success but points left. The missing neighbor's page was never
recorded. Its failed characterization and the superseded `flow-replay-fixed`
snapshot remain intact; that snapshot is not a corrected performance control.

The complete correction uses existing **structural** edge enumeration in
acquisition, Flow sampling and selected-edge-constrained recovery rays. Dormant
authored neighbors retain their ordinal and their pages are recorded before the
existing evaluator rejects them as impassable. Ordinary rays, volume numbering,
world/graph validation boundaries and ownership are unchanged. This can consume
additional existing candidate/dependency allowances in sparse queries; it adds
no capacity, cache or budget exemption. In the cross-page case, the old guide
now becomes stale and a later acquisition points right. Immediate acquisition's
existing one-time stale-incumbent response remains unchanged and is asserted
explicitly, followed by a fresh request on the next fixed frame.

A third public regression independently failed when constrained rays still used
physical numbering, then passed with structural replay. Its initial expected
portal coordinate assumed cell corners rather than integer-centered cells;
that test error was corrected from the authored foot anchors, retaining the
exact heading assertion. All failed runs are preserved. Independent review
approves the final three-site correction and tests. Focused validation passes
**973 Release / 968 Lean**, with zero warnings, errors, failures or skips, all
482 input hashes unchanged and the 158-file Release runtime unchanged through
Lean (`sparse-flow-canonical-reviewed-focused`). Full validation remains pending.
The new `flow-replay-complete` parent was frozen before the separate prism-reuse
optimization; the incomplete correction's snapshot was not reused.

### Flow Prism Reuse — Retained, Original Gate Still Red

Sampling already obtains the exact source and target prisms while reading node
state. Reuse those existing outputs instead of looking up the same two prisms
again. Both counted prism checks, dependency/world validation and body checks
remain unchanged; no state, cache, helper or public API is added. An existing
ten-row budget test now also retries with the exact successful allowance and
asserts the target, heading, medium and absence of a transition. It passes before
the optimization; the resulting focused matrix passes 973 Release / 968 Lean.
Independent correctness and simplicity review found no blocker.

The unchanged five-arm protocol produced these Actual **64-frame block medians**
in milliseconds; these are not individual-frame medians:

| Case | Original before | Complete parent before | Candidate | Complete parent after | Original after |
| --- | ---: | ---: | ---: | ---: | ---: |
| AStar/100 | 141.84270 | 108.47500 | 108.36590 | 108.41860 | 141.53010 |
| Flow/100 | 62.13940 | 65.08830 | 63.87280 | 65.69975 | 61.90650 |
| Flow/500 | 329.02730 | 343.34645 | 338.41710 | 351.25305 | 328.15180 |

**Retain this small reuse:** Flow/100 improves 1.87% / 2.78% against the two
corrected-parent controls, and Flow/500 improves 1.44% / 3.65%. AStar is flat
against that parent. However, Flow still costs 2.79–3.18% more than the originals;
AStar retains its 23.43–23.60% gain. The bracket does not isolate the structural
bugfix's cost against the earlier AStar-only snapshot.

Variation matters: Flow/500 parent medians drift 2.30%, and candidate process
medians span 333.30–345.40 ms. Its individual-frame P95 is 5.8949 ms, versus
5.8726 / 6.0547 ms for the parents, so not every tail improves. All 5,760 Actual
Flow/500 frames exceed 2 ms; none exceeds 31.25 ms. Candidate AStar has 30 such
2 ms misses and Flow/100 has none. There are three fresh process replications
per arm, not thousands of independent frame replications.

All 15 processes / 1,845 records completed without retries, with exact replay,
ray, frame-allocation and GC arrays matching. Narrow frame allocations and GC
counts are zero; broader BDN diagnostics separately report four Flow rows at
960 B and 26 at 1,296 B, with all 15 AStar rows at zero. No whole-operation
zero-allocation claim follows. Collector counters and runtime flags are unchanged.
Final source/runtime provenance was verified at 2026-09-18T12:28:24.2015660Z.
Raw evidence remains under ignored `flow-prism-reuse-*` artifacts. Full validation
and the unexplained original setup exception remain separate open gates.

### Full-Suite Follow-up — Exact Structural Candidate Accounting

The first full validation after prism reuse (`final-correctness-gates-v5`)
stopped at Release normal tests: 3,039 core passes and six failures out of 3,045,
with 65 adapter passes. The solution builds both target frameworks without
warnings or errors. Lean, explicit core coverage, Linux and documentation stages
were not run after that failure. Source and output integrity checks passed.

All six failures belong to the existing explicit-connection-leg budget theory.
Its sparse fixture authors cells 0–65 but materializes only 63–65. Structural
enumeration now correctly charges dormant neighbor 62, native neighbor 64, a
missing explicit owner and two parallel connections: five candidates during
search, then the same five during selected-route reconstruction. The old
eight-edge allowance silently exhausted before the final connection-leg cases.

Independent source review derives the corrected exact candidate totals of
4 / 5 / 10 at the three route stages. The test now separates its edge and leg
allowances and adds the one-below edge boundary: nine edges with six available
connection legs must fail after nine candidates and four legs; ten edges and
six legs must succeed with the exact cheaper parallel-route cost. All zero-slice,
status, dependency and failed-payload assertions remain. No runtime budget or
production behavior changes. All eight focused Release rows now pass with zero
warnings, errors or skips, and the production DLL is byte-identical to the
measured prism candidate. The failed V5 evidence is retained unchanged.

Fresh V6 validation passes both full solution builds for `netstandard2.1` and
`net8.0` without warnings or errors. Windows normal and explicit coverage runs
pass **3,046 Release / 2,968 Lean** core tests, with **65 adapter tests** per
configuration. Both configurations have exact core coverage of **32,108/32,108
lines, 13,164/13,164 branches and 3,158/3,158 covered and fully covered methods**;
adapter coverage remains exactly 186 lines, 28 branches and 11 methods. No new
exclusions, tolerances or retries were used.

Linux passes the same four suites from byte-verified Windows outputs, without
failures, errors, skips or unrun tests. DocFX has zero warnings/errors; all eight
site assets, five authored content checks, the workflow link checker across 135
HTML pages, four wiki rewrite tests and the runnable example pass. Final proof
at 2026-09-18T12:58:34.2231541Z confirms source/input inventories, Windows/Linux
runtimes, the frozen measured binary and upstream repository identities remained
unchanged. These correctness gates do not waive the remaining performance gap
or explain the original-baseline interval-sort exception.

### Latest Diagnostic And Next Scope Boundary

A single setup-only native feasibility run against copied, byte-verified
original binaries completed in **47.020 seconds**, including the deliberate
inspection pause and normal cleanup. Both the native range-check breakpoint and
filtered first-chance `IndexOutOfRangeException` stop were armed; neither fired.
The process used .NET 8.0.29 Workstation GC and SOS 9.0.12. All 59 original files
and the 61-file driver runtime remained unchanged. The three-minute watchdog did
not fire. Current optimized Sort/Sift/Trace code and code-version histories were
captured; there was no failing array/count pair to inspect. This is another
bounded non-reproduction, not a fix or acceptance evidence.

The temporary driver initially had two assembly-version conflict warnings.
Explicit references to its already-frozen dependencies corrected that build
before the sole runtime launch; the initial build evidence remains intact.
Read-only debugger-command syntax corrections are also retained in the log.
No library source, JIT/GC settings, production guard or exception suppression was
changed. Evidence is under ignored `sortintervals-setup-only-native-feasibility`.

Independent source review found no further supported Trailblazer-only shortcut.
The identity-matrix idea does not target the normally turning measured agents,
and vector normalization already has its safe normalized-vector fast path.
Freshness checks and previous-cell occupancy cleanup must remain.

### World-Owned GridForge Occupancy Registry — Ownership Fix, Timing Inconclusive

On 2026-09-18 the user approved this focused upstream change after reviewing the
occupancy-corrected baseline attribution. The user considers a small cost for
correct behavior potentially acceptable; this is not evidence that every part
of the latest Flow difference is explained, nor a waiver for scheduling overhead.
Keep the untouched-original comparison visible separately from incremental gains.

GridForge's existing lazy occupancy-registry reference now lives on its owning
`GridWorld`, removing the process-global dictionary lookup. Record contents,
identity/ticket checks, grid-to-registry lock order, reset callbacks and ordinary
reset reuse remain unchanged. Deactivation atomically detaches the captured
registry before clearing it. There is no per-Navigator cache, new public API,
dependency or stronger promise about concurrent mutation during world teardown.

The test-first ownership regression demonstrates that the old static dictionary
keeps an otherwise unreferenced, reset world alive; it fails on the original and
passes after relocation. Public first-registration concurrency and reset-callback
characterizations also pass. Independent review rejected a general-purpose lazy
initializer because its return rereads the detachable field; the retained atomic
publication returns the captured winner instead. Test cancellation now joins both
registration workers before disposing their world and gates.

The first full coverage run exposed the unexecuted losing-publication branch.
A deterministic test now exercises that private cold boundary against a real
winning registration, then checks public ticket resolution and deregistration.
An intentionally rejected-candidate return makes the test fail; the restored
captured-winner return passes. No production callback hook, timing-dependent retry,
coverage exclusion or weakened assertion was introduced. Final independent
correctness and simplicity reviews have no outstanding findings.

The predeclared comparison completed original-before / corrected-parent-before /
candidate / parent-after / original-after, three fresh processes per arm, using
the unchanged guided-frame harness. Only GridForge's DLL/PDB differ between the
parent and candidate; Trailblazer and the benchmark host remain identical there.
The original host is the same retained original used by the previous comparison,
not a new original/modern-host hybrid. These are medians of complete **64-frame
blocks in milliseconds**, not individual frames:

| Case | Original before | Parent before | Candidate | Parent after | Original after |
| --- | ---: | ---: | ---: | ---: | ---: |
| AStar/100 | 141.93230 | 108.93950 | 111.22280 | 114.30625 | 146.38100 |
| Flow/100 | 61.91580 | 63.83045 | 64.57105 | 65.40555 | 63.18845 |
| Flow/500 | 326.04025 | 341.47015 | 350.55615 | 356.55550 | 345.11385 |

Against the first/second corrected-parent controls respectively, the candidate
costs **+2.10% / -2.70%** for AStar/100, **+1.16% / -1.28%** for Flow/100 and
**+2.66% / -1.68%** for Flow/500. Parent control drift is 2.47–4.93%; original
control drift is 2.06–5.85%. That prevents a causal speedup, slowdown or
performance-neutral claim. Candidate Flow/100 frame P99 is 1.175 ms versus
1.133/1.155 ms for its parents; tail improvement is not established either.
Against the untouched originals, candidate AStar/100 is 21.64–24.02% faster,
Flow/100 2.19–4.29% slower and Flow/500 1.58–7.52% slower. Do not replace these
with the previous capture's tighter 2.8–3.2% Flow range or pool drifting controls.

All 15 processes completed with no retry, replacement or exclusion. Literal
movement/replay/ray arrays match, measured frame allocation/GC arrays stay zero,
and final source/runtime/dependency integrity checks pass. There are only three
independent process replications per arm, not 86,400 independent frame samples.
Every measured Flow/500 frame still exceeds 2 ms; no measured frame exceeds
31.25 ms. Broader BenchmarkDotNet allocation diagnostics remain separate from
zero allocation inside the measured frame regions. Raw results, process medians,
tails and full provenance are under ignored
`artifacts/navigator-recovery/world-owned-registry-performance`.

Fresh full Windows Release/Lean verification passes GridForge's **986** tests,
Trailblazer's **3,046 / 2,968** core tests and **65** adapter tests per configuration.
Both target frameworks build without warnings/errors. Exact line/branch/method
coverage remains 100%: GridForge **9,104 / 3,953 / 1,132**; Trailblazer
**32,108 / 13,164 / 3,158**; adapter **186 / 28 / 11**. The same six suites pass
on Linux using exact copies of the verified Windows-built runtimes. Gravitas's
full Windows local-source suites pass **3,950 Release / 3,895 Lean** tests against
the matching GridForge build, with both target frameworks building cleanly.
GridForge and Trailblazer DocFX and workflow link checks pass; Trailblazer's wiki
rewrite tests and runnable example also pass. No tests failed or were skipped.
Evidence belongs under `world-owned-registry-correctness-v2`. The unexplained
original-baseline interval-sort failure is separate and remains unresolved.
This ownership correction supplies no performance-gate proof and does not close
`TRB-Benchmark-005`; the user's subsequent acceptance direction is separate.

### Review Handoff And Remaining Scope

On 2026-09-18 the user committed GridForge's world-ownership correction and
authorized committing Trailblazer's implementation on `codex/navigator-recovery`
for whole-branch review. No additional recovery implementation task is identified
by the final scope review. Fresh precommit checks pass **3,046 core + 65 adapter**
tests in Release and **2,968 + 65** in ReleaseLean, without failures or skips.
The 482 Trailblazer and 195 GridForge source/build/test inputs and all four tested
runtime trees match the previously validated hashes. Tests ran without rebuilding
or restoring those exact outputs; unchanged coverage/Linux/Gravitas results above
were not recollected or misrepresented as new runs. Evidence is retained under
`artifacts/navigator-recovery/precommit-20260918`.

The user subsequently completed review and authorized the squash into `develop`.
This plan is archived with that integration; no push or release is authorized.
Keep the
latest Flow/100 **+2.19–4.29%** and Flow/500 **+1.58–7.52%** original comparisons
visible alongside control drift and the earlier 2.8–3.2% result; the willingness
to accept modest cost is not a new numerical equivalence threshold.

After review, return to `TRB-Benchmark-005` using the hardened implementation as
the working baseline while retaining the original comparisons. Target measured
remaining controller/steering/sampling cost and useful-guidance latency, not
removal of validated occupancy or freshness checks. Scheduling remains opt-in:
the documented total-work, first-use and waiting-latency costs still apply, and
neither a universal 2 ms solution nor a suitable default crowd preset is claimed.
The unexplained original-baseline sorting failure remains recorded above and is
separate from this feature's corrected regressions; preserve any fresh recurrence
for causal diagnosis rather than calling it fixed or repeating inconclusive runs.

## Execution Checklist

- [x] Trace acquisition, recovery, queue ownership and moving-origin constraints.
- [x] Reproduce the lifecycle signal and locate sampled recovery cost.
- [x] Confirm opt-in behavior and smart-queue requirements with the user.
- [x] Independently review the design and evidence; resolve connector lifetime,
  failed-adoption exits, comparison/cache controls and readiness accounting.
- [x] Review this contract, then freeze harness/settings and repeated baseline.
- [x] Implement shared frame accounting and bounded multi-request progression.
- [x] Implement compatible in-flight sharing with independent result ownership.
- [x] Implement Navigator waiting, direct admission, adoption and cancellation.
- [x] Complete serialization, behavior/coverage and evergreen documentation.
- [x] Measure the implemented candidate against the frozen controls and retain
  the full latency, cost and first-use tradeoff.
- [x] Complete independent reviews and record the actual remaining benchmark gap.
- [x] Correct allocation measurement with causal evidence, strict controls and
  fresh full Windows/Linux and exact-coverage verification.
- [x] Complete matched Immediate comparisons, preserve adverse observations and
  the separate original-baseline failure, and review the residual-cost tradeoff.
- [x] Finish fresh precommit verification for the authorized branch commit.
- [x] User reviews and accepts the branch; archive the plan with the authorized squash.

### Execution Record

- 2026-09-17: Branch created with the approved design and coordination edits
  preserved. Before runtime changes, the local-source solution baseline passes
  Release (2,662 core + 65 adapter) and ReleaseLean (2,590 core + 65 adapter),
  with no failures or skips. Logs/TRX/coverage inputs are under
  `artifacts/navigator-recovery/baseline-tests`; final coverage verification is
  still a separate gate. The first implementation task freezes the recovery
  harness and repeated immediate-mode controls before changing runtime behavior.
- 2026-09-17: Recovery harness and internal build/cache/promotion diagnostics
  passed independent review after fixing original-request readiness to remain
  latched independently of later live guidance. The focused guide/cache/scheduler
  checks passed in Release and ReleaseLean (146 each); the real readiness
  regression passed in both. No acquisition behavior changed before freezing
  `artifacts/navigator-recovery/baseline-frozen` and its runtime/source manifests.
- Two complete baseline captures are retained as `baseline-capture-a.log` and
  `baseline-capture-b.log`, with analyzed JSON beside them. Each has three fresh
  processes, 15 cases, 45 warmup episodes and 135 actual episodes. All 270 actual
  episodes succeeded without failed, cancelled or censored original requests;
  all 15 replay/readiness histories match within and across captures. Every
  frozen runtime file still matches its SHA256 manifest. Measurements below are
  this protocol's **baseline**, not an improvement over the earlier single-run
  diagnostic.

| Baseline immediate case | Median episode peak A / B (ms) | Median containing-frame sum B (ms) | Last usable frame |
| --- | ---: | ---: | ---: |
| A*/100 cold | 5,241.889 / 4,762.468 | 4,762.468 | 1 |
| A*/100 warm | 7.924 / 7.662 | 7.662 | 1 |
| A*/100 rebake | 4,903.468 / 4,855.473 | 4,855.473 | 1 |
| A*/100 profile change | 5,033.240 / 4,905.737 | 4,905.737 | 1 |
| A*/100 restore | 4,875.450 / 4,787.447 | 4,787.447 | 1 |
| Flow/100 cold | 236.276 / 226.143 | 226.143 | 1 |
| Flow/100 warm | 6.070 / 5.860 | 5.860 | 1 |
| Flow/100 rebake | 232.407 / 227.658 | 453.433 | 2 |
| Flow/100 profile change | 236.706 / 228.436 | 228.436 | 1 |
| Flow/100 restore | 6.276 / 6.234 | 6.234 | 1 |
| Flow/500 cold | 260.632 / 248.733 | 248.733 | 1 |
| Flow/500 warm | 27.256 / 26.602 | 26.602 | 1 |
| Flow/500 rebake | 256.167 / 252.750 | 479.598 | 2 |
| Flow/500 profile change | 252.953 / 250.098 | 250.098 | 1 |
| Flow/500 restore | 28.894 / 29.703 | 29.703 | 1 |

Each table cell summarizes nine actual episodes per capture; all individual
frames, allocations, allowance misses, process-wide CPU observations and
outcomes remain in the retained records. Every baseline recovery frame exceeded 2 ms.
Cold, rebake and profile-change frames also exceeded 31.25 ms, as did A* restore;
occasional Flow/500 warm/restore frames exceeded that ceiling in capture A.
Process CPU readings are coarse/quantized on this Windows host: a zero reading
for a short warm episode does not mean zero CPU work.

The counters establish one Flow build for cold/profile change, two builds plus
one promotion for rebake, and no builds for warm/restore. A* performs 100 builds
for each distinct-origin cold/rebake/profile/restore burst and none for warm.
Flow already shares its cold field in immediate mode; the candidate must improve
frame cost and useful-guidance latency without claiming fictitious 500-to-one
build savings. Spreading the A* stress case cannot erase its roughly five seconds
of work; report that latency tradeoff explicitly. Shared-frame implementation
began only after these controls were retained and verified.

- Shared-frame accounting and multi-request progression are implemented and
  independently approved. Actual remaining lookup/candidate/node/edge/connection
  credit passes through the existing admission/search pipeline. Atomic evaluator
  calls debit lookup credit at the evaluation boundary; query-total budgets stay
  separate. A finite operation ceiling and retained FIFO scan position bound
  cache/phase/publication/scan work without repeatedly refilling counters.
  Exact node-boundary tests include reconstruction, copying and reused Flow
  clearing. Release passes 2,688 core + 65 adapter tests; ReleaseLean passes
  2,616 core + 65 adapter tests. Both library targets build in both configurations
  with no warnings/errors. This is a correctness checkpoint, not a timing or
  final exact-coverage claim. Cohort sharing and Navigator ownership remain next.

- Frozen compatible cohorts and independently owned follower leases are
  implemented and independently approved. Tests cover bounded formation,
  incompatible interleaving, cancellation before/during builds, detached
  payload sharing, capacity-one transfer, exact proof-byte limits, endpoint-only
  invalidation and independent semantic actions. The narrow scheduled rebake
  regression now proves one fresh Flow build and no promotion, while its
  immediate control retains the prior outcome. Release passes 2,739 core tests
  and ReleaseLean 2,667; both library targets build without warnings/errors.
  These are correctness/counter results, not measured frame improvements.
- Cohort testing also reproduced `TRB-Issue-136`: an ordinary Flow cache hit can
  discard dependency proof consumed by a different origin's endpoint admission.
  A separately reviewed fix now certifies ordinary and shared cache-hit admission
  against the payload, or reserves and retains the missing proof independently.
  Certified warm hits remain allocation-free; extra dependency comparisons debit
  the original query budget and frame allowance. Exact proof-byte, cancellation,
  cached-leader clone/transfer and endpoint-only invalidation tests pass. Release
  passes 2,766 core tests and ReleaseLean 2,694; both targets build without
  warnings/errors. The added ordinary-hit comparison cost remains part of the
  forthcoming candidate-immediate measurements, not a claimed speedup.

Navigator integration now exposes opt-in scheduled acquisition through shared
steering, including direct-first checks, current-foot Flow adoption, explicit
waiting/retry and schema-7 configuration. Independent review passed after fixing
a pathing-only reset that released a completed ticket without changing its public
Success outcome: steering now recognizes lost ownership and retries on a later
frame without losing intent. Eight graph/direct A*/Flow reset regressions prove
the fix; focused Release/Lean ownership suites pass 352/303 tests. The preceding
full checkpoint passed 2,815/2,741 core tests plus 65 adapter tests in each
configuration. The final full matrix remains required after moving-origin A*
connectors and documentation hardening.

Moving-origin A* connectors and lifecycle documentation are implemented and
independently approved after a permit-order fix. A retained connector now reaches
its charged boundary before ordinary mode-specific validation can discard it or
start recovery. Four stale-route/snapshot-capacity regressions failed before the
fix and pass afterward; exact/one-below workspace checks and a 60-frame moved-foot,
publication, mode-switch and permit-wait replay also pass. Final task-checkpoint
Release passes 2,891 core + 65 adapter tests; ReleaseLean passes 2,813 + 65, with
both target frameworks building without warnings. Two earlier allocation-guard
failures remain unattributed in this plan; later green runs do not
establish their cause. Exact coverage, platform/site gates and formal comparative
performance remain in progress.

Final hardening also reproduced and fixed ordinary A* endpoint-proof loss on an
identical-key cache hit (`TRB-Issue-144`), using the existing bounded certification
and lease-local proof mechanism. Independent review approved the source and
meaningful boundary tests. Release and Lean each reach exact core coverage of
32,029 lines, 13,068 branches and 3,155 fully covered methods; each adapter reaches
186 lines, 28 branches and 11 fully covered methods. Both target frameworks build
without warnings/errors. Lean passes 2,918 core + 65 adapter tests; copied portable
outputs pass on Linux (Release 2,996 + 65, Lean 2,918 + 65). DocFX, local site links,
wiki checks and runnable examples pass. Linux execution is not a Linux source-build
claim. A later test-only continuation-loop bound passes its two focused cases in
both configurations without changing runtime or allocation-guard behavior.

The final uninstrumented Windows Release solution run passed 2,995/2,996 core
tests and 65 adapter tests; the volume allocation guard reported 3,064 bytes.
Frozen failing binaries and build-source hashes are retained. Bounded profiler
and temporary phase-counter diagnostics did not identify an allocator; their
passes do not fix or replace this failure. No guard, warmup, tolerance or runtime
workaround remains. The independent benchmark controls and comparison proceed
without waiving that unresolved merge/release gate. Details and distinct later
observations remain in this plan's allocation regression, not in evergreen user
documentation or a separate backlog item.

Early Flow sizing on an explicitly retained intermediate candidate demonstrates
the expected peak/latency tradeoff, not final acceptance. With one explicit tuned
allowance, the second Flow/500 cold episode completed in 28 frames (0.875 simulated
seconds), with a 25.896 ms peak and 386.319 ms containing-frame sum; same-map rebake
completed in 29 frames with one build, a 15.101 ms peak and 279.076 ms sum. The
same process's first-use observations reached 97.343 ms, so this is not a hard
31.25 ms guarantee. Smaller allowances reduced peaks but substantially increased
waiting and cumulative host work. All 100 exploratory records, including first
use, are retained; final paired captures must use the completed implementation.

#### Separate Guided-Frame Baseline

The unchanged balanced-phase guided-frame cases were also captured from the
frozen baseline assemblies. BenchmarkDotNet's in-process toolchain avoids
rebuilding against the changed checkout; each capture uses three fresh outer
processes, ten warmup blocks and thirty actual 64-frame blocks per case. Captures
`steady-baseline-a` and `steady-baseline-b` each retain 270 actual blocks
(17,280 containing frames), plus separate warmup and diagnostic records. Replay
and per-agent periodic-ray histories match within and across captures. Early
and late actual windows are broadly stable after this longer warmup; an earlier
one-warmup sizing run was not stable and is not used for acceptance.

| Existing immediate guided case | Median frame A / B (ms) | Observed P99 A / B (ms) | Maximum A / B (ms) |
| --- | ---: | ---: | ---: |
| A*/100 | 2.244 / 2.230 | 2.631 / 2.542 | 3.855 / 3.266 |
| Flow/100 | 0.954 / 0.940 | 1.097 / 1.079 | 1.412 / 1.430 |
| Flow/500 | 4.995 / 4.970 | 5.865 / 6.111 | 7.086 / 6.935 |

These are baseline controls, not candidate improvements. No frame exceeded
31.25 ms. All A*/100 and Flow/500 frames exceeded 2 ms; none of the Flow/100
frames did. The initial acquisition and preparation are outside these timings,
and correlated nested frames are not independent statistical trials. Compare
the implemented candidate using this same frozen-assembly protocol separately
from recovery episodes and their usable-guidance latency.

#### Candidate Acceptance Settings

Final captures use one frozen candidate build, with the
same three-launch, one-warmup/three-actual recovery protocol twice for each arm:

- Immediate mode at unchanged work limits, for all original populations/cases.
- Scheduled mode at work limits `65536,128,4096,8192,64,256`, for all original
  populations/cases. The values are lookup, candidate, node, edge, connection,
  and operation limits, respectively.
- Scheduled Flow/100/500 at unchanged default work limits, and separately at
  `131072,256,8192,16384,64,512`, to retain the observed latency/peak tradeoff.
  A Flow-only companion at `65536,128,4096,8192,64,256` uses the same launch
  history for the work-limit comparison. These subset launches omit the earlier
  A* cases present in the all-population controls; compare their settings with
  one another, not as matched speedup ratios against all-population Immediate.
- Existing immediate balanced-phase guided cases using the separate baseline's
  ten-warmup/thirty-actual block protocol, twice.

Retention remains workload-sized exactly as in the controls, not the public
default capacity of eight requests. Default-work-limit A*/100 receives an
explicit censored diagnostic if it reaches the unchanged 10,000-frame safety
cap; that is a latency failure, not an accepted successful capture. No runtime
default changes, extra warmup hidden inside setup, or dropping first-use
observations are implied by these settings. All settings and original request
outcomes remain in the raw records. Final result tables will distinguish
baseline-to-candidate Immediate changes from the same-build scheduling tradeoff.

#### Final Recovery Measurements

The frozen final candidate was measured on the same Windows/.NET 8 local-source
stack as the baseline. Each ordinary arm retains two captures, each with three
fresh processes and one warmup plus three actual episodes per case: nine actual
episodes per table cell per capture. All original requests in these accepted
arms became ready, with no failures or censoring and exact within-mode replay.
Raw records and complete summaries are under `artifacts/navigator-recovery/final/`.
The final independent evidence review reproduced the protocol, results and
provenance, accepting the evidence with the concerns below.

The old profiler session remained explicitly **Paused**, not removed: its stop
operation returned access denied, and the shared service was not restarted.
Before/after service counters accompany each arm. The first Immediate arm added
15.625 ms service CPU and no I/O; subsequent recovery arms added neither CPU nor
I/O. No earlier active-export measurements are used here. This is a recorded
experimental condition, not a claim that profiling infrastructure was absent.

**Immediate regression control.** The following are medians of each episode's
maximum containing-frame time, in milliseconds. A/B are the two captures, not
minimum/maximum bounds. All original cases are retained, including warm/restore.

| Population / trigger | Baseline Immediate A / B | Candidate Immediate A / B |
| --- | ---: | ---: |
| A*/100 cold | 5241.889 / 4762.468 | 4711.991 / 4735.879 |
| A*/100 warm | 7.924 / 7.662 | 7.816 / 7.947 |
| A*/100 rebake | 4903.468 / 4855.473 | 4741.018 / 4940.311 |
| A*/100 profile | 5033.240 / 4905.737 | 4840.298 / 4901.972 |
| A*/100 restore | 4875.450 / 4787.447 | 4646.524 / 4746.646 |
| Flow/100 cold | 236.276 / 226.143 | 216.622 / 226.349 |
| Flow/100 warm | 6.070 / 5.860 | 5.854 / 5.940 |
| Flow/100 rebake | 232.407 / 227.658 | 222.166 / 225.749 |
| Flow/100 profile | 236.706 / 228.436 | 221.194 / 227.995 |
| Flow/100 restore | 6.276 / 6.234 | 6.220 / 6.374 |
| Flow/500 cold | 260.632 / 248.733 | 243.131 / 256.098 |
| Flow/500 warm | 27.256 / 26.602 | 27.034 / 28.538 |
| Flow/500 rebake | 256.167 / 252.750 | 248.699 / 262.576 |
| Flow/500 profile | 252.953 / 250.098 | 244.992 / 257.824 |
| Flow/500 restore | 28.894 / 29.703 | 29.552 / 31.044 |

Changes are mixed, not an Immediate speedup claim. The largest paired increase
in these peak medians is about 7.3% for Flow/500 warm in capture B. Readiness
remains one frame except Flow rebake's two frames. The latter's median frame
sum is 470.671 / 496.228 ms for candidate Flow/500, not the peak shown above.

**Same-build scheduling tradeoff.** Scheduled B uses the explicit allowance
defined above and the same all-population process order as Immediate. Each
first/median/last readiness triplet is identical in both captures. The frame
sums include normal controller work on every waiting frame, not isolated search
time or an equal-duration post-arrival gameplay comparison.

| Population / trigger | Scheduled peak median A / B (ms) | Frame sum median A / B (ms) | First / median / last ready frame |
| --- | ---: | ---: | ---: |
| A*/100 cold | 24.223 / 24.064 | 5454.448 / 5459.901 | 6 / 184 / 361 |
| A*/100 warm | 3.250 / 3.251 | 13.568 / 13.449 | 3 / 4 / 6 |
| A*/100 rebake | 24.325 / 23.830 | 5443.226 / 5476.247 | 6 / 184 / 361 |
| A*/100 profile | 27.690 / 27.611 | 5591.482 / 5634.588 | 6 / 184 / 361 |
| A*/100 restore | 23.750 / 24.014 | 5415.713 / 5452.081 | 6 / 184 / 361 |
| Flow/100 cold | 8.244 / 8.196 | 242.678 / 245.271 | 45 / 45 / 47 |
| Flow/100 warm | 2.738 / 2.698 | 8.032 / 8.125 | 3 / 4 / 5 |
| Flow/100 rebake | 7.944 / 7.985 | 245.469 / 247.408 | 47 / 48 / 49 |
| Flow/100 profile | 8.084 / 8.058 | 242.676 / 244.855 | 47 / 48 / 49 |
| Flow/100 restore | 2.619 / 2.614 | 8.148 / 8.348 | 3 / 4 / 5 |
| Flow/500 cold | 8.939 / 8.960 | 340.051 / 341.488 | 46 / 51 / 57 |
| Flow/500 warm | 7.207 / 6.975 | 72.493 / 71.427 | 4 / 10 / 16 |
| Flow/500 rebake | 8.855 / 9.037 | 343.445 / 346.638 | 48 / 53 / 59 |
| Flow/500 profile | 8.726 / 8.782 | 338.005 / 341.267 | 48 / 53 / 59 |
| Flow/500 restore | 7.201 / 6.953 | 71.537 / 71.500 | 4 / 10 / 16 |

Flow/500 cold peak medians fall **96.32% / 96.50%**, but frame sums rise
**39.86% / 33.34%** and last-ready latency becomes 57 frames, or 1.78125 simulated
seconds at 32 Hz. A*/100 cold retains 100 distinct builds and waits 361 frames
(11.28125 simulated seconds); that is not a responsive general-purpose preset.
Both retain the same cold expanded-node/edge totals as Immediate. Warm Flow/500
still builds nothing, but scheduling raises the frame sum by 150–168% and
measured episode allocation from zero to 287,280 bytes. Opt-in is material.

Same-map Flow rebake does remove work: Scheduled uses one build and no promotion
instead of Immediate's two builds and one promotion. Flow/500 finalized-query
nodes fall from 19,785 to 9,895 and evaluated edges from 464,462 to 232,278.
Its frame sum falls **27.03% / 30.15%** despite waiting; measured episode allocation
falls from 1,876,720 to 1,289,568 bytes. Cold Flow/500 allocation instead increases
from 771,568 to 1,058,632 bytes. These counters exclude unfinished work, direct
rays, sampling and some copying; they are not a complete CPU-work accounting.

Process-wide CPU supports the same caution: median Flow/500 cold episode CPU
is 250 / 250 ms in candidate Immediate versus 359.375 / 359.375 ms Scheduled B;
warm is 31.25 / 31.25 versus 78.125 / 78.125 ms. CPU includes validation and
background/JIT/GC activity across the episode, not just timed search, and its
quantization limits small comparisons. Collection-overlap observations and
per-episode allocations remain in the raw summaries; overlap does not establish
GC as an outlier's cause. Baseline and candidate benchmark instrumentation are
not byte-identical, although the measured frame boundary and scenarios match.

All actual Scheduled-B Flow frames fit 31.25 ms in these captures. A* has four
misses in capture A and two in B; its maximum is 61.879 ms. Many frames still
exceed 2 ms. Of 16,308 actual containing frames per Scheduled-B all-population
capture, 15,548 / 15,553 exceed 2 ms. Warmup maxima reach **120.985 / 128.932 ms**,
with 55 / 63 frames above 31.25 ms; cold-cache actual episodes are not the same
as first process use. Nine episodes with correlated nested frames cannot
establish a portable tail bound.

**Matched Flow-only allowance sensitivity.** These omit the earlier A* process
history and therefore compare only with one another, not by direct ratios with
the all-population Immediate table. Flow/500 cold results are:

| Work allowance | Peak median A / B (ms) | Frame sum median A / B (ms) | Last ready frame / simulated seconds |
| --- | ---: | ---: | ---: |
| Default | 6.583 / 6.091 | 4777.755 / 4380.501 | 2087 / 65.219 |
| B | 8.238 / 8.352 | 315.379 / 314.977 | 57 / 1.781 |
| C | 15.508 / 15.106 | 278.457 / 275.088 | 28 / 0.875 |

The larger C allowance trades higher peaks for less waiting; it is an example
for this workload, not a changed default. Its first/median/last Flow/500 cold
readiness is 23/25/28 frames. Default limits are unsuitable for this crowd burst
even with workload-sized retention; capacity alone does not grant processing
credit. C's Flow/500 cold maximum is 15.881 / 15.812 ms, while B's second capture
includes a 21.551 ms outlier. Neither is silently dropped.

Across all ten Flow-only cases, actual maximum / warmup maximum in captures
A and B are respectively: Default **33.291 / 35.473 ms** and **7.639 / 12.080 ms**;
B **16.538 / 52.435 ms** and **21.551 / 55.473 ms**;
C **25.234 / 95.593 ms** and **24.162 / 98.633 ms**.
Default capture A has one actual 31.25 ms miss; B and C have none in actual
episodes, but their first-use observations do exceed it. These are observed
maxima across the stated cases, not deadline guarantees.

The separate unchanged-default A*/100 cold diagnostic reached the fixed
10,000-frame cap with **82 ready and 18 censored**, no terminal failures, 83
builds started and 82 completed. It exported its single raw warmup record before
the expected nonzero acceptance exception. No successful summary or repeat
replaces that latency failure; 10,000 frames already represent 312.5 simulated
seconds at 32 Hz.

Candidate-only diagnostics also retain every original request: interleaved
compatible destination groups finish Scheduled in 91 frames with two builds;
an uncovered origin finishes in 68 with two builds and one promotion. This
proves sharing does not pretend a partial field covers every origin. Retention
eight versus 100 finishes the same 100-agent cold workload in 69 versus 47
frames, with one build in both; 92 distinct agents observe capacity pressure
with eight slots. Repeated status observations are not submission-attempt counts.
These controls retain all six exact cases; they have no invented historical
baseline.

#### Initial Feature-Handoff Guided-Frame Control

The unchanged Immediate balanced-phase cases were measured from the final frozen
candidate using the same three-process, ten-warmup/thirty-actual block protocol
as the baseline. Each capture retains 270 actual blocks and 17,280 actual frames;
replay checks pass. These measurements exclude initial acquisition.

| Case | Candidate median frame A / B (ms) | Candidate observed P99 A / B (ms) | Candidate maximum A / B (ms) | Paired median 64-frame block change A / B |
| --- | ---: | ---: | ---: | ---: |
| A*/100 | 2.311 / 2.292 | 2.932 / 2.575 | 3.390 / 4.479 | +4.5% / +2.2% |
| Flow/100 | 1.012 / 0.992 | 1.213 / 1.142 | 1.595 / 1.377 | +6.5% / +4.9% |
| Flow/500 | 5.480 / 5.292 | 6.446 / 6.191 | 7.181 / 6.963 | +9.0% / +6.6% |

This is an apparent **steady-state regression**, not a recovery benefit or a
demonstrated source attribution. Early/late windows within candidate launches
are broadly stable; both repeated captures are slower in block medians. No frame
exceeds 31.25 ms, but every A*/100 and Flow/500 frame exceeds 2 ms, as in the
baseline. Flow/100 has no 2 ms misses. Different capture times and launch-level
variation prevent calling small differences a precise causal effect, but they
do not justify dismissing the repeated increases. The blocking-regressions
section above owns its fixes and current verification; `TRB-Benchmark-005`
remains the broader coordination item, not a place to defer this regression.

The opt-in recovery behavior has a substantial measured spike reduction and
avoids duplicate rebake work. That does **not** complete the broader 2 ms target,
make the current A* burst latency acceptable, or erase the Immediate control
regression. The separate failed allocation gate was also unresolved at this
initial handoff; its subsequent diagnosis and verification are recorded above.

#### Independent Source Review

The initial whole-branch correctness review found no new blocking runtime defect.
It traced shared frame accounting, frozen cohorts, admission/currentness fences,
ticket and connector teardown, Immediate-mode behavior, moving-origin heading
authorization, lifecycle/actions, and schema 7 staged population. All 297 source
files in the reviewed snapshot matched the live source. The separate Ponytail
review found no material simplification that preserves these contracts; the
Navigator uses the existing scheduler rather than a second queue.

Both reviews kept acceptance separate from source approval. At this checkpoint,
the original Windows Release allocation failure remained open. The then-final
benchmark candidate's Trailblazer, GridForge, FixedMathSharp and SwiftCollections
DLL hashes match the frozen final Release test runtime; subsequent test-only and
benchmark changes did not alter those binaries. No merge or release is approved.

The separate final evidence audit independently rehashed the baseline and
candidate runtimes/source overlays, matched raw records to summaries, reproduced
the principal comparisons and checked steady replay/ray histories and collector
counters. Its only numeric correction, Flow/100 steady rounding to 6.5% / 4.9%,
is addressed. It accepts the evidence **with concerns**, not unconditional
performance acceptance. The subsequent user-directed regression investigation,
focused fixes and all unfavorable controls are recorded above; passing later
runs do not erase the original failures.

**Initial handoff disposition:** retain the implemented opt-in capability on the review branch,
but leave this plan active and unmerged. The user requires fixing both the
Immediate steady-state and zero-allocation gate regressions before completion.
The next useful performance work is to isolate the regression, not add another
scheduler abstraction or tune away unfavorable observations. No upstream source
change, default-policy change, commit, push or release was made in this slice.

Separately reproduced pre-existing defects belong in [issue-tracker.md](../issue-tracker.md);
feature-branch regressions remain in this plan. The
documented scheduler latency and prefix-rebuild mechanisms are design constraints,
not newly confirmed correctness defects. Keep `TRB-Benchmark-005` open until its
broader performance goal is genuinely met or its disposition is explicitly reviewed.
