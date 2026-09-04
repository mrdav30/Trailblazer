# Issue Tracker

## Tracker Rules

- Issue IDs use `TRB-Issue-NNN`. The next available ID is `TRB-Issue-104`.
- Assign an ID when an issue enters this tracker, keep it through resolution,
  and never reuse an ID even if an entry is later removed. Check this file's Git
  history before advancing or repairing the counter.
- Add new items when feature work uncovers a suspected bug, stale doc, test
  smell, performance anomaly, or correctness risk.
- Keep each item scoped tightly enough to fix and verify independently.
- Record the date on the item, not in this filename.
- Move an item to `Resolved Issues` only after the fix has tests or documented
  verification evidence.
- Do not use this tracker as a substitute for tests, benchmarks, or release
  notes.
- Performance issues should stay in
  [`benchmark-signal-hardening-backlog.md`](benchmark-signal-hardening-backlog.md)
  unless they become a confirmed runtime defect. Do not add performance issues
  here until they have been investigated and confirmed as runtime defects.

## Active Issues

### TRB-Issue-103 - Local source graph can build SwiftCollections with two assembly versions

- **Discovered:** 2026-09-03
- **Area:** Cross-stack `UseLocalLsfStack` validation through GridForge
- **Status:** Active; package-backed integration and direct project validation
  are not blocked, but solution-wide local-stack validation is unreliable.
- **Evidence:** After restoring the affected generated assets to the default
  package cache, `dotnet build Gravitas.slnx --configuration Release --no-restore
  --property:UseLocalLsfStack=true` reaches compilation and fails with `CS1704`
  for two `SwiftCollections` assemblies. (A preceding `NETSDK1064` was unrelated
  stale generated metadata from an isolated NuGet cache.) GridForge's local
  stack directly pins `SwiftCollections.csproj`
  to assembly version 7.0.0.0 and also references
  `SwiftCollections.FixedMathSharp.csproj` as version 7.1.0.0. That bridge's
  unqualified nested project reference can propagate the bridge's
  `SemVer`/`AssemblySemVer` into a second SwiftCollections project instance,
  producing 7.1.0.0 beside the intended 7.0.0.0. Configuration-less solution
  graph traversal makes the duplicate visible. Direct configuration-specific
  Gravitas source builds, Gravitas test-project runs, and the shared Trailblazer/
  Gravitas probe pass in both standard and Lean modes.
- **Impact:** A contributor following the solution-wide local-stack workflow
  receives a duplicate-assembly compile error even though every released
  package family is aligned.
- **Expected:** The owning lower-stack project must give its nested
  `SwiftCollections.csproj` reference the core library's explicit 7.0.0 version
  identity and configuration instead of inheriting the bridge package's 7.1.0
  identity. Do not add a binary-copy workaround or compatibility package path.
- **Verification:** From clean generated outputs, restore and build GridForge,
  Gravitas, and Trailblazer with `UseLocalLsfStack=true` in `Release` and
  `ReleaseLean`; assert one SwiftCollections project instance/version per target
  and zero warnings. Then rerun all package-backed matrices to prove the local-
  source fix did not alter released dependency ownership.

### TRB-Issue-102 - Ground contact conflates the support normal with platform orientation

- **Discovered:** 2026-09-03
- **Area:** Trailblazer ground-contact ownership and Gravitas integration
- **Status:** Active; slope-enabled Gravitas integration is blocked.
- **Evidence:** Gravitas stores `SolidBody.GroundNormal` independently from the
  supporting collider's `FixedTransform`. Trailblazer instead computes
  `GroundCondition.GroundNormal` only as `Platform.Transform.Up`, then uses that
  value for slope angle, projection, sliding, and ground-jump direction. On an
  identity-transformed wedge or mesh face, the honest platform transform yields
  `Up` even when the physical contact normal is sloped. Rotating the snapshot to
  encode the contact normal corrupts moving-platform attachment and carry.
- **Impact:** A host cannot preserve both collision geometry and carrier motion.
  Trailblazer can classify a real slope as flat or move an attached actor from a
  fabricated carrier orientation.
- **Expected:** Replace the derived-only `GroundNormal` contract with one
  explicit, serialized `GroundCondition.SurfaceNormal` world-space value that is
  independent of `PlatformSnapshot.Transform`. `Vector3d.Zero` is the sole
  no-sample value; the runtime must not infer `Platform.Transform.Up`. A host that
  wants flat-ground behavior supplies `Vector3d.Up`. Update `SetGroundContact`,
  cloning, motor behavior, XML, and wiki guidance without exposing a compatibility
  alias. Keep Trailblazer core free of Gravitas types.
- **Verification:** First reproduce an unrotated static sloped face and assert
  the exact slope/projection normal. Then cover a translating/rotating carrier
  whose contact normal varies independently. Increment the outer Navigator
  schema version; old payloads are rejected by the existing exact-version gate
  rather than silently defaulting or deriving a normal. Verify JSON and
  MemoryPack round trips, explicit `Zero` and `Up` behavior, exact coverage, and
  the full `Release`/`ReleaseLean` matrix.

## Resolved Issues

The issues below were resolved and verified as part of the 2026-09-01 coverage
hardening and code-quality follow-up passes.

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
  bounded scope lookup as `while (true)`, and preserved an A* double-bind test
  that bypassed the guide cache's detach-before-rent protocol.
- **Impact:** Mixed guard conventions obscured which checks remain required in
  Release, the unconditional loop hid its deterministic scope bound, and the
  internal-abuse test made an impossible pool state look like supported behavior.
- **Expected:** Route exact public, lifecycle, serialization, and corruption
  guards through `SwiftThrowHelper`; retain direct throws only where the helper
  cannot preserve the exception contract or terminal control flow; keep proven
  internal ownership as diagnostic assertions; express ingress work with an
  explicit bound; and remove the hollow double-bind test.
- **Verification:** Ninety-three exact guard sites now use `SwiftThrowHelper`,
  the 37 remaining direct throws have documented semantic reasons, scope removal
  performs one allocation-free bounded scan, and supported A* pooling remains
  covered. Full `Release` passes 2,287 tests, full `ReleaseLean` passes 2,238,
  and both aggregates cover all 30,067 lines, 11,761 branches, and 2,952 methods.

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
  undefined statuses even though both production callers handle or exclude
  those states before mapping.
- **Impact:** Unsupported test inputs made impossible mapper defaults look like
  public behavior and obscured the exact terminal-status contract.
- **Expected:** Test only reachable terminal statuses and assert the caller-owned
  input invariant inside each mapper.
- **Verification:** The focused search contract suite and both exact coverage
  aggregates pass with six hollow rows removed.

### TRB-Issue-096 - Failed full-rebuild reconciliation leaves stale readers admitted

- **Discovered:** 2026-09-01
- **Area:** Topology lifecycle safety and operation reconciliation
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A post-snapshot dynamic-slot capacity rejection could occur after
  an unrelated topology removal requested a full seam rebuild but before the
  computed all-close graph published.
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
- **Expected:** Transfer rejected-operation rollback to the pending full rebuild;
  expose only no graph or an all-closed graph until exact topology publication.
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
- **Impact:** The full Lean suite fails before its independent coverage result can
  represent the supported Lean package surface.
- **Expected:** Keep JSON coverage in both configurations and compile the
  MemoryPack row only when the transport is present.
- **Verification:** Run the focused theory and exact aggregate in Release and
  ReleaseLean.

### TRB-Issue-093 - Completed seam lifecycle revalidates an impossible concurrent mutation

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam lifecycle completion
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `MaintainAutomaticSeamLifecycle` calls `RevalidateForPublication`
  immediately after `AdvanceOne` reports complete on the same deterministic
  maintenance boundary. Earlier world changes are returned as stale by the
  worker; only an unsupported concurrent mutation between the two calls can make
  the completion revalidation fail.
- **Impact:** The Release restart arm is unreachable through ordered context
  simulation and would require a probabilistic race test to cover.
- **Expected:** Keep same-boundary revalidation as a diagnostic ownership
  assertion and publish the completed result through the singular lifecycle path.
- **Verification:** Preserve stale-before-completion restart tests and run exact
  Release and ReleaseLean coverage.

### TRB-Issue-092 - Runtime baseline scan retains an impossible blocked-owner skip

- **Discovered:** 2026-09-01
- **Area:** Chunked default-baseline capacity lifecycle
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `AdvanceCoveredBaselineRebuilds` skips retained rebuilds marked
  capacity-blocked, but runtime-owned rebuilds are rejected, replaced, or removed
  at the capacity boundary before a later scan can observe that terminal flag.
  The one-below integration fixture retains an incomplete rebuild with a blocked
  count of zero, while direct worker tests already own the local flag behavior.
- **Impact:** The runtime condition suggests a terminal owner can persist into a
  later maintenance scan even though lifecycle ownership prevents that state.
- **Expected:** Remove the impossible runtime skip while retaining direct bounded-
  worker capacity behavior.
- **Verification:** Run the focused graph-capacity fixture and include it in exact
  Release and ReleaseLean coverage.

### TRB-Issue-091 - Completed seam work rechecks its proven capacity allowance

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam lifecycle completion
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationAutomaticSeamLifecycleWork.AdvanceOne` receives the
  exact remaining retained-work allowance, yet completion reclassifies the result
  as restart if the same graph-level capacity check fails.
- **Impact:** The second predicate models an impossible divergence between the
  bounded worker and its caller and leaves an unreachable completion branch.
- **Expected:** Keep the invariant as a diagnostic assertion and let publication
  revalidation alone choose complete versus restart.
- **Verification:** Run automatic-seam capacity/lifecycle tests and exact Release
  and ReleaseLean coverage.

### TRB-Issue-090 - Writer-preflight publication fallbacks model unsupported races

- **Discovered:** 2026-09-01
- **Area:** Graph-runtime closure publication and rollback
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** After the maintenance writer has passed `CanPublish`, automatic-
  seam start, composition rollback, and no-change closure reopening retain
  fallback branches that can fail only if an external reader races the same
  single-threaded deterministic maintenance boundary.
- **Impact:** The branches cannot be reproduced through supported ordered context
  simulation, encourage probabilistic race tests, and preserve direct classifier
  tests that do not prove public behavior.
- **Expected:** Express the writer-ownership guarantee diagnostically and keep the
  supported deterministic publication path singular.
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
- **Impact:** The focused behavior can pass against stale binaries yet fail in the
  authoritative rebuilt aggregate after the runtime fix is present.
- **Expected:** Require the supported affected-catch-up boundary to be observed
  and remove temporary trace scaffolding.
- **Verification:** Rebuild the focused test, confirm its affected-root assertions
  pass, and include it in exact Release and ReleaseLean coverage.

### TRB-Issue-088 - Volume-anchor coverage injects an unowned node reference

- **Discovered:** 2026-09-01
- **Area:** Volume-anchor evaluation tests and graph ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A direct evaluator test passes `default(NavigationNodeRef)` even
  though production callers resolve the node from the same immutable graph before
  evaluation.
- **Impact:** The test preserves a Release fallback for a state that supported
  endpoint and segment paths cannot supply, mixing invalid-state coverage with a
  useful dependency-capacity assertion.
- **Expected:** Remove the invalid-node row, express graph ownership as a
  diagnostic invariant, and retain the atomic dependency-capacity behavior.
- **Verification:** Run the focused volume-anchor fixture and include it in exact
  Release and ReleaseLean coverage.

### TRB-Issue-087 - Transition refresh test bypasses duplicate-rule validation

- **Discovered:** 2026-09-01
- **Area:** Transition-rule publication tests
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A test composes a raw graph with duplicate global transition-rule
  IDs specifically to bypass the map-fold validation that rejects that state in
  the supported candidate pipeline.
- **Impact:** The invalid-state fixture preserves a Release throw branch that no
  accepted operation can reach and duplicates the real validation test.
- **Expected:** Keep duplicate ownership rejection at the supported fold boundary
  and express refresh-time uniqueness as a diagnostic invariant.
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
- **Expected:** Collect and enforce an independent exact summary for both Release
  configurations without merging unlike assemblies.
- **Verification:** Exercise the Ubuntu Release/ReleaseLean CI matrix and confirm
  each summary independently reports exact line, branch, covered-method, and
  fully-covered-method totals.

### TRB-Issue-085 - Coverage workflow can validate a different commit after failure

- **Discovered:** 2026-09-01
- **Area:** CI workflow ownership and merge gates
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The post-`build-and-test` workflow checks out the branch tip rather
  than `workflow_run.head_sha`, does not require the triggering run to succeed,
  and runs only after pushes to `main`.
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
- **Evidence:** Three large tests in `NavigationGraphValueSemanticsTests` combine
  transition-page behavior, internal key/reference validity, dependency identity,
  and budget accounting in single methods.
- **Impact:** A failure in one contract obscures the exact behavior that regressed
  and can prevent later independent contracts in the same method from running.
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
- **Expected:** Remove invalid-enum rows and collapse internal default handling to
  the proven supported-medium invariant where necessary.
- **Verification:** Confirm the invalid casts and unreachable branches are absent,
  then rerun exact Release coverage plus the Release/ReleaseLean matrix.

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
- **Verification:** Run focused graph-capacity and lifecycle fixtures, then rerun
  exact Release coverage plus the Release/ReleaseLean matrix.

### TRB-Issue-080 - Materialized catch-up drops an owned affected closure

- **Discovered:** 2026-09-01
- **Area:** Structural-composition and materialized-snapshot closure transfer
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `MaintainMaterializedComponentWork` copies only
  `ClosedStructuralComponents` and the all-close flag from a requested structural
  publication. An exact affected closure stores its operation-owned component set
  in the graph's additional closed root, so the copied graph can omit that root.
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
- **Area:** Structural-composition capacity and physical-capture regression tests
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `PhysicalChangeCarryover_ShouldRetainExactCaptureUntilComponentPublication`
  authors cells through index 7 against a normalized grid ending at index 1,
  so `NavigationMapBuilder.Build` rejects the fixture before publication.
  `ResumedComposition_ShouldRejectDynamicSlotOverflowWithoutLeakingSafetyClosure`
  configures eight overlay cells but only one dynamic slot, so context settings
  validation rejects both theory rows before the claimed capacity lifecycle.
- **Impact:** Three coverage rows are red for malformed setup and provide no
  evidence for physical-capture retention or resumed-composition rollback.
- **Expected:** Keep every authored cell inside normalized bounds and align the
  one-cell overlay limit with the one-slot lifetime ceiling so the fixtures reach
  their intended deterministic runtime boundaries.
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
  the obstacle update during carryover and the fully reconciled map afterward, and
  rerun exact Release coverage plus the Release/ReleaseLean matrix.

### TRB-Issue-077 - Composition capacity rollback loses its closure baseline

- **Discovered:** 2026-09-01
- **Area:** Retained materialization and structural-composition rollback ownership
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
- **Evidence:** `NavigationStructuralCompositionWork.Advance` receives the
  exact allowance remaining after current-root, operation, baseline, and rebuild
  ownership. When it reports capacity, its retained state already exceeds that
  allowance. The immediately following combined check adds the operation-owned
  closed root and tests the same configured aggregate ceiling, so it necessarily
  rejects every state the earlier boolean rejected.
- **Impact:** Two adjacent rejection paths model one ownership boundary and make
  it appear that initial worker growth and aggregate closed-root capacity can
  disagree.
- **Expected:** Let the combined `work + closed root` check own both budget-yield
  and capacity-yield cases, preserving one atomic initial-composition rejection.
- **Verification:** Preserve preflight rejection, measured worker-growth
  rejection, terminal closure reopening, and exact Release coverage.

### TRB-Issue-074 - Terminal composition rollback models an impossible immediate reopen

- **Discovered:** 2026-09-01
- **Area:** Operation-owned structural closure rollback
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `BeginOperationClosureRollback` is called only for
  `ReleaseAndBeginRollback`, which requires a published operation result with
  composition or materialized completion ownership. Every such result either
  published during the current maintenance pass, retains full-rebuild
  ownership, or still has ingress ownership. The fallback that immediately
  called `TryPublishReopenedStructuralScopes` therefore cannot execute through
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
- **Evidence:** Twelve internal graph identity structs override
  `Equals(object)` even though every runtime owner compares them through their
  exact generic type and `IEquatable<T>`. The only wrong-runtime-type caller is
  an unconstrained coverage helper. Four of those structs also expose equality
  operators, forcing the boxed override solely to satisfy the compiler even
  though all operator call sites are internal and can use the typed contract.
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
  retains the full candidate plus a 3,856-byte owner and succeeds only while
  the current root, candidate, and work fit together. Starting the seam then
  retains the same closed candidate plus only 3,560 bytes. The page envelope is
  likewise strictly smaller than the already-proven current-root and
  materialized-work envelope. Retained materialized completion repeats the same
  capacity proof immediately before its start call.
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
  second-bind guards with their reflected fixtures, remove duplicate returns
  and boxed-key paths, and exercise guide/slot generation retirement through
  production-owned pooling and no-wrap policies.
- **Verification:** Preserve ordinary guide reuse, public stale-alias fail-close,
  sample-ordinal overflow policy, disposed/full pool behavior, and permanent
  retirement of exhausted guide and lease-slot identities; then rerun the exact
  Release coverage aggregate without the reflected rows.

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
  retained work fits that allowance; successful completion and seam
  revalidation add no retained ownership before the repeated check.
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
- **Area:** A* coincident guide-point ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** A non-node candidate following a node is preserved by the first
  disposition, and any candidate following a non-node is replaced by the
  second disposition. Reaching the append disposition therefore requires both
  the prior and candidate points to be distinct addressed nodes.
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
  When completed seam revalidation fails, `ResetSeamState` therefore always
  sees `_affectedClosurePublished` and sets `_allCloseRepublishRequired`.
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
- **Verification:** Keep zero-new-address overlay materialization, positive exact
  delta, oversized-delta rebuild, and full empty-map capture behavior, then
  confirm the impossible skip is absent from the next `Release` coverage
  aggregate.

### TRB-Issue-061 - Seam changed-map capture rechecks its assigned ownership phase

- **Discovered:** 2026-09-01
- **Area:** Structural composition automatic-seam changed-map capture
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The resumable seam cursor has only phase 0 and phase 1. A phase-0
  budget failure returns immediately; every non-returning phase-0 path reads one
  changed map and assigns phase 1 before control reaches the ownership block.
  A resumed dependency-budget failure also retains phase 1.
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
- **Impact:** The forwarding overload is zombie code with no runtime behavior
  or useful test surface, and retaining it prevents exact method/line coverage
  from describing the executable library accurately.
- **Expected:** Remove the unreferenced overload and retain the production-used
  closed-interval predicate shared by portal and target policies.
- **Verification:** Confirm no call sites remain, keep the before/boundary,
  interior, and after interval matrix, and restore exact line/method coverage
  in the next full Release aggregate.

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
  capacity, and operation rollback behavior coverage, then confirm the
  redundant publication check is absent from the next full `Release` coverage
  aggregate.

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
  materialization, and operation-composition capacity coverage, then confirm
  the nullable composition fallbacks are absent from the next full `Release`
  coverage aggregate.

### TRB-Issue-059 - Final ray admission rechecks an impossible post-segment interval entry

- **Discovered:** 2026-09-01
- **Area:** Navigation-ray final target admission
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** GridForge `GridTraceInterval` values are exact intersections with
  the traced segment and normalize `TEnter` and `TExit` to its closed `[0, 1]`
  parameter range. `TryFinish` reads one of those retained intervals, so its
  `TEnter > Fixed64.One` rejection cannot occur. `TExit < Fixed64.One` remains
  meaningful because it proves that an ordinary final interval does not contain
  the requested destination; `DestinationSuffix` intentionally bypasses that
  requirement.
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
  normalized configurations, and each resolved map retains that same
  normalized binding. Repeating `TryGetSeamPrism` for either contact endpoint
  therefore cannot fail.
- **Impact:** Treating either repeated prism lookup as ordinary missing seam
  geometry models no supported contact and hides a broken GridForge/graph
  binding invariant behind a silent return.
- **Expected:** Assert both endpoint-prism ownership checks diagnostically,
  while preserving navigation-portal construction failure as a supported
  geometry outcome.
- **Verification:** Keep physical-contact coverage for mapped and unmapped
  navigation addresses plus incompatible portal geometry, then confirm both
  redundant prism-miss branches are absent from the next full `Release`
  coverage aggregate.

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
  window after the broad warmup, then enforce the exact per-instance contract
  on the following window for both seam-refresh and seam-lifecycle work.
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
- **Verification:** Keep repeated exact-scope coalescing, multi-medium expansion,
  and one-entry dependency-budget regressions, then confirm the duplicate pair
  branch is absent from the next full `Release` coverage aggregate.

### TRB-Issue-052 - Source-row seam filtering tolerates an impossible absent source record

- **Discovered:** 2026-09-01
- **Area:** Automatic-seam row reconstruction
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `ShouldFilterSourcePair` is called only for a pair emitted by the
  source automatic-seam row. A matching pair delta is created from the same
  source index's exact `TryGetPairRecord` result before row reconstruction. The
  source row and pair-record index are immutable parts of one
  `NavigationAutomaticSeamIndex`, so a matching delta reached through that row
  cannot have a null `SourceRecord`.
- **Impact:** Treating the source record as optional invents geometry and active
  defaults that no valid seam index can own, and can hide a broken internal
  index invariant behind ordinary row filtering.
- **Expected:** Assert source-record ownership diagnostically and filter
  dependency rows by geometry changes while filtering active rows by geometry
  or activity changes.
- **Verification:** Keep the mixed mapped/unmapped seam behavior coverage and
  cover the complete dependency-versus-active row filtering policy, then
  confirm the nullable source-record fallbacks are absent from the next full
  `Release` coverage aggregate.

### TRB-Issue-051 - Graph node readers revalidate immutable traversal ownership

- **Discovered:** 2026-09-01
- **Area:** Graph node-state and structural native-edge enumeration
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Every exact-medium `TryGetNodeState` caller retains a medium that
  was validated when its query, transition, or evaluator was created; the
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
- **Impact:** Rechecking `!invalidated` after `outputCount > 0` models a producer
  result GridForge cannot return and leaves an unreachable branch in default
  baseline discovery.
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
  semantics therefore published a tombstone without assigning the stable
  dynamic slot needed to materialize it. Existing compose-only tests manually
  supplied the expected address registry and did not exercise the production
  fold.
- **Impact:** A default-cell tombstone can be omitted from the published graph,
  so a cell intended to be blocked may continue inheriting passable default
  semantics.
- **Expected:** Unbaked `Set` and default-backed `Suppress` operations both
  reserve a stable dynamic address. Once assigned, that identity remains
  reserved across suppression and reversion so later map replacement cannot
  absorb or reuse its slot.
- **Verification:** Keep the production fold regression covering direct
  default-backed suppression plus permanent reservation across reversion and
  explicit-only Set-to-Suppress changes, then run
  focused map/graph tests and the full Release and ReleaseLean matrices before
  moving this item to resolved.

### TRB-Issue-046 - Internal area-policy lookups retained impossible null branches

- **Discovered:** 2026-09-01
- **Area:** Pathing graph dependency validation and guide/ray admission
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationAreaCatalog.TryGet` returns `true` only after assigning
  an element from its immutable non-null policy array, yet its nullable `out`
  contract forced callers such as `TrailblazerGuideService` and
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
  64-address baseline scratch capacity, `NavigationMapInstance.CopyNewCanonicalAddresses`
  returns zero because the map's total address count exceeds the scratch span.
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
  obstacles to all eight GridForge cells before the next frame makes the
  overlay publication fail with `CapacityExceeded` and enter
  `BeginOperationClosureRollback`. The immediately published rollback graph has
  an open structural scope while its first cell still reports
  `IsMaterialized == true`, `ObstacleCount == 0`, and `IsBlocked == false`, even
  though the committed world contains the obstacles and a physical resnapshot
  has been requeued.
- **Impact:** Navigation can observe stale passable physical state for at least
  one frame after a capacity-rejected operation instead of remaining
  fail-closed until committed GridForge changes are reconciled.
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
  context-owned simulation time. Missing-context validation then throws, but
  the earlier state mutations remain visible.
- **Impact:** A failed public jump command is not transactional. Reusing or
  subsequently binding the object can inherit a jump that never successfully
  started, producing state divergence from the command result.
- **Expected:** Required context state must be validated and captured before
  mutating jump lifecycle fields; a failed registration must leave the object
  unchanged.
- **Verification:** Keep a standalone regression that asserts the exception and
  exact pre-call state, then run the complete jump and controller lifecycle
  suites in `Release` and `ReleaseLean`.

### TRB-Issue-006 - A* snapshot pressure discards active steering intent

- **Discovered:** 2026-08-31
- **Area:** Navigation steering and A* guide lease refresh
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** With an active public A* steering query and every graph snapshot
  lease held, the current guide reports `CapacityExceeded`. The next heading is
  correctly zero while pressure remains, but `ValidateGraphMovementPath`
  handles only `Stale` as retryable and returns false. `GetHeading` then calls
  `Arrive()` and clears `CurrentQuery`; the focused regression expected the
  exact original query and observed `null`. Flow sampling already treats the
  equivalent `CapacityExceeded` status as retryable.
- **Impact:** Temporary graph-snapshot capacity pressure permanently cancels an
  A* movement request instead of pausing and retrying it, so otherwise
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
  can crash graph maintenance instead of retaining and atomically publishing
  the exact affected address. The failure occurs after bounded work resumes,
  so low maintenance budgets make it directly reachable in normal operation.
- **Expected:** Exhausting the final overlay map must finish the raw-scope
  cursor and return to `HasNextRawMapId` without indexing past the frame-change
  batch. The exact component and address debits must remain resumable and the
  overlay must publish without stale membership.
- **Verification:** Keep the one-cell suppress regression with one dependency
  entry per frame red before the fix, then prove exact component/address retry,
  successful completion, and suppressed effective state in focused `Release`
  and `ReleaseLean` graph tests.

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

### TRB-Issue-009 - Stale copied A* reservation can release its replacement

- **Discovered:** 2026-08-31
- **Area:** A* payload cache reservation ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationAStarPayloadReservation` contains only maximum bytes
  and a lease-slot flag. Reserving A, copying it, releasing A, reserving B for
  the same byte count, and then releasing the stale copy of A passes cache
  validation and consumes B's accounting. Releasing the legitimate B
  reservation then throws `InvalidOperationException` from
  `NavigationAStarPayloadCache` because the counters are inconsistent. The
  focused regression is deterministic and uses only normal reservation APIs;
  Flow reservations already carry owner, slot, and generation identity.
- **Impact:** A stale value copy can corrupt cache byte and lease-slot ownership,
  causing valid A* publication/release operations to fail and potentially
  making capacity availability depend on alias order.
- **Expected:** Each A* reservation must have exact cache-owned identity so a
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
  combined allowance by one makes GridForge report
  `CandidateWorkLimitExceeded`, but `NavigationVolumeAnchorEvaluator` maps the
  result to `CapacityExceeded` instead of `BudgetExceeded`.
- **Impact:** A Flow guide can report workspace exhaustion when only its public
  per-sample work budget ran out, preventing callers from distinguishing a
  retryable budget boundary from a fixed capacity limit.
- **Expected:** Candidate-work exhaustion derived from
  `NavigationWorkMeter.RemainingGridCandidateWork` must report
  `BudgetExceeded`; workspace grid, address, and output ceilings must continue
  to report `CapacityExceeded` at their exact boundaries.
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
  `NavigationRayWork.MapExplicitStatus` converts it to `Stale`; the selected
  ray therefore reports `Stale` instead of `Blocked`.
- **Impact:** A structurally current guide can be invalidated as stale merely
  because its agent is too large for one authored connection, obscuring a
  normal geometric blockage and triggering unnecessary cache or query refresh.
- **Expected:** Explicit-edge profile impassability must report `Blocked` while
  structural certificate failures remain `Stale` and checked arithmetic remains
  `CostOverflow`.
- **Verification:** Keep the legal clearance matrix covering passable,
  off-corridor blocked, and too-wide blocked outcomes, then run focused ray and
  guide tests in `Release` and `ReleaseLean`.

### TRB-Issue-013 - Synchronous A* rejection publishes through an absent search

- **Discovered:** 2026-08-31
- **Area:** A* query admission and publication lifecycle
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
  then prove the complete class and focused A* orchestration slice in `Release`.

### TRB-Issue-014 - A* pending action leaks an undefined public status

- **Discovered:** 2026-08-31
- **Area:** A* guide status projection
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationAStarGuideLease.TryAdvanceWaypoint` legitimately
  returns internal `Pending` when the current step is a semantic action. The
  arithmetic public mapper subtracts one from that zero-valued status and emits
  byte value 255 instead of the documented fail-closed `Stale`. Existing ladder
  and explicit-transition regressions observe the undefined enum value. The
  sibling Flow request mapper has the same arithmetic default hazard. Removing
  A* guide generation retirement also permits an eventually wrapped generation
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
- **Evidence:** Retiring the sole preallocated guide at `ulong.MaxValue` correctly
  keeps that shell out of the reuse pool while recycling its payload lease slot.
  The next valid guide acquisition then calls `RentGuideUnderLock` with
  `_freeGuideCount == 0`; the method decrements to `-1` and throws
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
  `PublicGuideRequest_ShouldReportCostOverflowWithoutLeakingALease` for the
  Flow algorithm with public status `CapacityExceeded` instead of
  `CostOverflow`. `NavigationFlowFieldWork.MapTraversalStatus` projected the
  internal traversal enum by subtracting one even though the Flow enum orders
  `CostOverflow` before `CapacityExceeded`.
- **Impact:** Callers cannot distinguish fixed-point path-cost overflow from
  workspace capacity exhaustion and may respond with the wrong recovery path.
- **Expected:** Flow search maps every internal terminal traversal status to its
  exact public guide meaning without relying on enum ordinal alignment.
- **Verification:** Keep the public A*/Flow guide regression, restore an
  explicit exhaustive projection, and prove the focused guide slice plus the
  fresh `Release` coverage aggregate pass.

### TRB-Issue-019 - Dormant automatic seams can be published as traversable

- **Discovered:** 2026-08-31
- **Area:** Automatic seam refresh and sparse physical-presence lifecycle
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The full `Release` coverage aggregate reproduced four seam
  lifecycle regressions after a publication-invariant simplification. A
  durable seam pair intentionally remained in the structural graph after its
  sparse endpoint disappeared, but `FillSeam` treated active-row membership as
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
- **Area:** Explicit surface-edge routing and A* guide construction
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
  runtime-significant corridor checks, and prove the focused surface A* slice
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
- **Area:** A* and Flow admission-to-search handoff
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Exact branch tracing found identical post-admission epoch checks
  in both query workers. Admission already rejects an epoch change before it
  binds a resolved query. If the world changes in the following concurrency
  window, cache checkout validates the payload epoch and a cache miss passes the
  original epoch into an A* or Flow worker that rejects it before doing search
  work.
- **Impact:** The duplicate early exits add two state-machine paths and a
  misleading coverage obligation without changing the public fail-closed
  `Stale` outcome.
- **Expected:** Keep epoch ownership in admission, cache validation, and the
  search workers; remove the duplicate handoff checks.
- **Verification:** Preserve the admission, cache, A*, and Flow world-mutation
  regressions, then run the focused query slices and a fresh `Release` coverage
  aggregate.

### TRB-Issue-023 - A* publication exposes an impossible capacity fallback

- **Discovered:** 2026-09-01
- **Area:** A* query publication and payload reservation ownership
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Caller tracing showed that a gate-owned reservation has exact
  cache ownership, remains current for the query lifetime, and is sized to the
  worker's maximum payload before search starts. After a completed search,
  `TryPublish` therefore cannot reject a current payload for capacity. The old
  fallback could be reached only by constructing query work around an
  undersized reservation outside the admission gate's invariants.
- **Impact:** The impossible `CapacityExceeded`/`NoPath` split suggests a
  recoverable runtime outcome that the real state machine cannot produce and
  obscures that publication failure at this boundary means stale proof.
- **Expected:** Assert the reservation invariant in diagnostic builds and map
  the only live publication failure to `Stale`.
- **Verification:** Keep exact cache reservation-capacity tests at the cache
  boundary, preserve the query dependency-mutation regression, and run the
  focused A* query slice plus a fresh `Release` coverage aggregate.

### TRB-Issue-024 - A* publication performs a non-authoritative immediate recheck

- **Discovered:** 2026-09-01
- **Area:** A* payload publication and guide handoff
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationAStarPayloadCache.TryPublish` validates the exact
  graph dependencies and optional world epoch before publishing. Query work
  immediately repeated those lock-free reads and removed the payload if they
  changed, but the world or graph could still change immediately after that
  second check. Definitive guide creation and guide use already revalidate the
  same proof.
- **Impact:** The best-effort duplicate check adds a race-only state-machine
  path without establishing a stronger currency guarantee.
- **Expected:** Treat cache publication as proof that the payload was current
  at publication time and leave subsequent currency validation to the guide
  consumer boundary that can act on it.
- **Verification:** Preserve cache publication, stale guide acquisition, and
  concurrent guide-use regressions; then run the focused A* concurrency slice
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
  medium from that immutable node, profile, and requested-media tuple, while
  the blocked-ray volume fallback retains the pending candidate's same address.
  Therefore a second candidate cannot reach `CanBeatCurrentResult` with the
  same `NavigationCellAddress` and a lower resolution medium; that tie arm has
  no constructible caller state.
- **Impact:** The zombie tie implies that one canonical address can resolve to
  competing media across endpoint candidates, obscuring the actual stable
  ordering contract and adding an untestable branch to deterministic ranking.
- **Expected:** Rank equal-distance candidates solely by canonical address;
  keep the deterministic per-node medium choice inside candidate qualification.
- **Verification:** Preserve the overlapping-endpoint canonical-selection and
  mixed-medium qualification regressions, then confirm the removed tie arm is
  absent from the next `Release` coverage aggregate.

### TRB-Issue-029 - Navigation trace mapping repeats an impossible map-directory miss

- **Discovered:** 2026-09-01
- **Area:** Ordered navigation-ray and swept-volume trace mapping
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** `NavigationWorldGraph.TryGetMapId` reads the immutable
  configuration index built from the graph's map instances. Every graph
  transformation either preserves that index with the same instance directory
  or constructs both from the same replacement instance set. A map id returned
  by that index therefore cannot be absent or null in the same graph's instance
  directory, yet both centerline interval mapping and swept-volume mapping
  treated that state as a recoverable stale trace branch.
- **Impact:** The duplicate lookup failure advertises an impossible partial
  graph state and obscures the real staleness boundary: GridForge world identity
  and change-sequence checks against an otherwise coherent immutable graph.
- **Expected:** Keep the configuration-index miss for physically traced grids
  with no navigation map, assert the index-to-directory ownership invariant,
  and retain all real world/grid generation mismatch checks.
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
- **Evidence:** `NavigationAreaCatalog` owns a private
  `NavigationAreaPolicy[]`. Its only successful `TryGet` path assigns the exact
  array element to the out parameter; every miss assigns `null` and returns
  `false`. Catalog construction and publication accept non-null policies and
  never store a null element. Therefore neither the expected nor current policy
  can be null after its corresponding lookup succeeds.
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
- **Impact:** Recoverable fallbacks for an impossible cursor-capacity failure and
  impossible zero-valued ownership counters obscure the exact rebuild state
  machine and create untestable branches in safety-critical baseline recovery.
- **Expected:** Express all three states as diagnostic ownership invariants while
  preserving the exact cursor begin, unique rediscovery, structural-state
  removal, and count consumption behavior.
- **Verification:** Preserve default baseline seed, sparse rediscovery, capacity,
  and interleaved-mutation regressions, then confirm the impossible fallback
  branches are absent from the next `Release` coverage aggregate.

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
  can be silently discarded, obscuring the canonical merge invariant and
  adding branches that no valid seam lifecycle can exercise.
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
  the owned closed graph publishes successfully. Every cleanup path releases
  the materialized worker before clearing that baseline. Consequently retained
  work cannot reach completed publication without its owned baseline, yet the
  publication path fell back to the empty component set when it was null.
- **Impact:** The fallback advertises a partially initialized ownership state
  and could hide lifecycle corruption by reopening against the wrong closure
  baseline instead of exposing the violated invariant.
- **Expected:** Assert baseline ownership diagnostically and publish completed
  materialized work against the exact retained baseline.
- **Verification:** Preserve retained materialization, closure rollback,
  automatic-seam lifecycle, and exact-capacity regressions, then confirm the
  successful-null fallback is absent from the next `Release` coverage
  aggregate.

### TRB-Issue-036 - Traversal paths repeat impossible same-graph reference resolution

- **Discovered:** 2026-09-01
- **Area:** Traversal enumeration, native state lookup, and volume-edge certification
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** Traversal and incoming enumerators receive medium states resolved
  from the same immutable graph they retain. Native and complete neighbor
  lookups, active seam rows, and explicit-route dependency evidence likewise
  produce nodes owned by that graph. Volume-edge endpoints therefore retain
  exact addresses and bound cell prisms for the evaluator lifetime.
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
  restart, and retained-work regressions, then confirm both impossible
  fallbacks are absent from the next `Release` coverage aggregate.

### TRB-Issue-040 - Baseline rebuild repeats an impossible configuration-key mismatch

- **Discovered:** 2026-09-01
- **Area:** Chunked physical-baseline identity validation
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** GridForge's `GridWorld.TryCaptureNavigationBaselineCore` resolves
  the active grid by the exact requested `GridConfigurationKey`, then constructs
  the successful `GridNavigationBaseline` with that same `configurationKey`
  value verbatim. A baseline returned for the map binding therefore cannot carry
  a different configuration key.
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
  completion to false. The only path that sets completion true immediately
  flows into `CompleteRuleContactScan`, which advances the rule and resets the
  phase before the enumerator can re-enter that scan state.
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
- **Area:** A* transition-guide zero-allocation regression
- **Status:** Resolved; verified by the 2026-09-01 release gates.
- **Evidence:** The full instrumented `Release` aggregate reported 3,248 bytes
  inside the single measured window of
  `AStar_ShouldReconstructExplicitTransitionWithoutTreatingItAsMovement`, while
  five isolated instrumented reruns of the same test reported zero. The guide
  behavior remained successful in every run, and the test already performed a
  fixed warmup before measuring the identical call loop.
- **Impact:** A one-time runtime or coverage-instrumentation transition can
  fail the release gate even though the post-transition hot path is allocation
  free, making the test order-sensitive instead of measuring steady state.
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
- **Verification:** Preserve removal, replacement, discovery, stale restart,
  and blocked-budget regressions, then confirm the redundant classifier is
  absent from the next full `Release` coverage aggregate.

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
  discovery regressions, then confirm the redundant predicate is absent from
  the next full `Release` coverage aggregate.

## Verification Record

### 2026-09-01 coverage hardening

- `Release`: 2,288 tests passed; 30,013 of 30,013 lines, 11,903 of
  11,903 branches, and 2,952 of 2,952 methods were covered. All 2,952 methods
  were fully covered.
- `ReleaseLean`: 2,239 tests passed with the same 30,013-line,
  11,903-branch, and 2,952-method production surface at exact coverage.
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
