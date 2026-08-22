# Phase 7 Volume, Transitions, And Unified Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the legacy Volume and experimental Hybrid stacks with one deterministic, graph-backed Solid/Gas/Liquid A*/Flow authority, explicit transition actions, and topology-native free-flight routing.

**Architecture:** Keep one physical graph node per effective cell and key bounded search state by `(NavigationNodeRef, TraversalMedium)`. Native/volume movement retains medium; semantic actions may retain or change medium. GridForge owns topology, swept-body coverage, portals, and prism-union geometry; FixedMathSharp owns exact fixed-point predicates; Trailblazer owns media, policies, costs, budgets, graph publication, guides, and controller orchestration. Port every live controller/serialization consumer before deleting the old Volume/Hybrid families.

**Tech Stack:** C# 11, .NET `netstandard2.1` + `net8.0`, xUnit v3, FluentAssertions, FixedMathSharp, SwiftCollections, GridForge, Chronicler JSON/MemoryPack, BenchmarkDotNet.

**Spec:** `docs/superpowers/specs/2026-08-20-volume-transitions-and-unified-routing-design.md`

## Global Constraints

- Determinism, correctness, maintainability, then performance.
- No floating-point runtime math, LINQ, BCL hot-path collections, topology formulas, local wide-math helpers, or engine APIs in Trailblazer.
- Reuse FixedMathSharp.Geometry, GridForge direction/portal/body geometry, and SwiftCollections.
- Develop behavior RED-first. Record the exact failing assertion/compiler boundary before production edits.
- Run .NET processes serially. Use one isolated Phase 7 package path/config for the local LSF stack.
- Keep one authoritative public API. Do not add compatibility aliases, forwarding overloads, optional compatibility arguments, test hooks, runtime callbacks, or inactive serialization modes.
- Preserve current surface A*/Flow behavior while widening internal state to media.
- Temporary old/new internals may coexist only until their direct consumers move; each task names its deletion boundary.
- Keep every preparatory default/rule/result/worker addition internal through Tasks 2-9. Task 10 is the sole atomic public rule/query/guide/controller/serialization cutover and legacy deletion boundary; no commit exposes competing public suites.
- Every mutable/publication path must preserve affected-only dependency invalidation and transactional failure.
- Each task ends with `git diff --check`, focused Release and ReleaseLean gates, a read-only correctness review, a read-only ponytail review, and one focused commit.

---

## Task 1: Add GridForge Swept-Body Union Coverage

**Repository:** `F:\gamedevrepos\GridForge`

### Task 1A upstream prerequisite: exact joint sweep relation

**Repository:** `F:\gamedevrepos\FixedMathSharp`

**Complete:** `7d2ac675d6193f4cd4a2408ebdfb09a96a05d74c`

- Add exactly one public boolean
  `FixedConvexPrismRelations.IntersectsSweptUprightCylinderStrict(...)` taking
  bottom-start/bottom-end, radius, full height, prism origin/rotation, ordered
  local footprint offsets, and prism half-thickness.
- RED the shared-parameter boundary where strict planar overlap begins exactly
  when strict vertical overlap ends. The result is false; one raw unit of real
  overlap is true; endpoint reversal preserves both decisions.
- Keep the exact rational parameter domain and wide quadratic/half-plane tests
  private. Do not publish rounded interval outputs, an exact-rational type, a
  cache/workspace, a general CSG abstraction, or a forwarding overload family.
- Cover stationary, individual and joint tangency, radius-zero strict interior,
  odd-raw height, rotated/reversed-winding 4/6-vertex prisms, invalid inputs,
  full-domain cases, and warmed zero allocation in Release and ReleaseLean.
- Commit FixedMathSharp first as
  `feat(geometry): test swept upright cylinders against convex prisms`; then
  GridForge consumes that exact commit through the local stack and isolated
  Phase 7 package feed.

**Files:**

- Add: `src/GridForge/Grids/Support/GridNavigationBodyTrace.cs`
- Add: `src/GridForge/Grids/Support/GridNavigationBodyTraceScratch.cs`
- Add: `src/GridForge/Utility/GridTracer.NavigationBody.cs`
- Modify: `src/GridForge/Grids/Topology/GridCellGeometry.NavigationBodySegment.cs`
- Modify: `src/GridForge/Utility/GridTracer.cs`
- Add: `tests/GridForge.Tests/Utility/GridNavigationBodyTraceTests.cs`
- Modify: `tests/GridForge.Tests/Grids/GridCellGeometryTests.cs`
- Add: `tests/GridForge.Benchmarks/Memory/GridNavigationBodyTraceBenchmarks.cs`
- Modify: `tests/GridForge.Benchmarks/Program.cs`
- Modify: `docs/wiki/GridTracer-and-Coverage.md`

- [x] Add RED rectangular 2-axis and 3-axis tests proving the fixed 4/8-cell corner closure is required and a missing closure cell rejects the union.
- [x] Add RED pointy/flat hex vertical-planar tests proving the four-cell closure and canonical output order.
- [x] Add RED large-body tests where radius/height positively overlaps cells outside the corner closure; assert canonical cross-grid coverage, exact tangency ownership, and physical sparse gaps without adding navigation-medium semantics to GridForge.
- [x] Adapt each issued prism to the Task 1A boolean relation and delete GridForge's independently rounded planar/vertical overlap-interval composition. GridForge owns topology and union selection, not exact wide intersection math.
- [x] Add RED dense/sparse, just-fit/equality, one-raw-fail, endpoint reversal, invalid geometry, exact capacity, one-below capacity, exact work budget, one-below budget, world mutation, and warmed zero-allocation cases.
- [x] Run the exact RED:

  `dotnet test tests/GridForge.Tests/GridForge.Tests.csproj -c Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~GridNavigationBodyTraceTests|FullyQualifiedName~GridCellGeometryTests"`

- [x] Implement one required, allocation-free caller-bounded API on `GridTracer`; do not add an allocating convenience overload:

  ```csharp
  public static GridNavigationBodyTraceReport TraceNavigationBodyInto(
      GridWorld world,
      WorldVoxelIndex source,
      WorldVoxelIndex target,
      Vector3d startFoot,
      Vector3d endFoot,
      Fixed64 horizontalRadius,
      Fixed64 bodyHeight,
      SwiftList<GridNavigationBodyTraceCell> results,
      GridNavigationBodyTraceScratch scratch,
      int addressCandidateLimit,
      int outputLimit,
      long candidateWorkLimit);
  ```

- [x] Have GridForge derive broad-phase coverage from the swept capsule/body bounds across adjacent grids, create exact topology prisms, retain only positive interior overlap under deterministic half-open boundary ownership, and validate the direct body segment through the complete returned prism union. Compose only exact congruent prisms in one aligned topology lattice; heterogeneous or misaligned partial-prism CSG fails closed.
- [x] Return distinct Complete, IncompletePhysicalCoverage, Invalid/UnrepresentableGeometry, AddressLimitExceeded, OutputLimitExceeded, and CandidateWorkLimitExceeded statuses with exact completed counters. Preserve canonical present/missing cell identity and world/grid high-water evidence for `IncompletePhysicalCoverage`; distinguish required coverage from missing exact-prism OR-alternative dependency evidence, and apply `outputLimit` atomically to both roles. Clear results only for invalid, budget, or capacity failures that cannot publish a reusable negative proof.
- [x] RED a missing closure/swept cell followed by physical insertion: the retained missing-cell evidence must stale the negative proof without staling an unrelated trace.
- [x] Keep topology direction/closure derivation in `RectangularDirectionUtility`/`HexDirectionUtility` or their topology implementations; do not recreate offsets in Trailblazer.
- [x] Add the benchmark alias/case with semantic counters for 4/8-cell and large-body coverage; no timing claim until canonical evidence.
- [x] Run focused Release + ReleaseLean tests, both source TFMs/configurations, benchmark Dry semantic preflight, and warmed allocation assertion.
- [x] Pack GridForge and GridForge.Lean into this plan's isolated Phase 7 feed and record the exact package versions/content; all later Trailblazer implementation gates use `-p:UseLocalLsfStack=true`, while Task 13 restores normal package mode only from those exact isolated artifacts.
- [x] Run correctness and ponytail reviews; remove any speculative descriptor/cache/template/maxima state.
- [x] Commit GridForge: `ece1aece4d83fea16c25e0a0e0181da2ea22b6a4` (`feat(geometry): validate swept body prism unions`).

## Task 2: Make Map Defaults And Transition Rules Immutable Authoring Truth

**Files:**

- Modify: `src/Trailblazer/Pathing/Map/NavigationMap.cs`
- Modify: `src/Trailblazer/Pathing/Map/NavigationMapBuilder.cs`
- Modify: `src/Trailblazer/Pathing/Map/NavigationMapTokenImporter.cs`
- Modify: `src/Trailblazer/Pathing/Map/Operations/PreparedNavigationMap.cs`
- Modify: `src/Trailblazer/Pathing/Map/Operations/NavigationMapFoldWork.cs`
- Modify: `src/Trailblazer/Pathing/Map/Overlay/NavigationCellOverlayOperation.cs`
- Modify: `src/Trailblazer/Pathing/Map/TraversalTransitionDefinition.cs`
- Modify: `src/Trailblazer/Pathing/Search/Ray/NavigationRayWork.cs`
- Modify: `src/Trailblazer/Pathing/Map/Operations/NavigationOperationLimits.cs`
- Modify: matching operation settings/default/test construction sites found by `rg`
- Add: `src/Trailblazer/Pathing/Map/TraversalTransitionRule.cs`
- Add: `src/Trailblazer/Pathing/Map/TraversalTransitionRuleScope.cs`
- Add: `src/Trailblazer/Pathing/Transition/TraversalTransitionLocomotionHints.cs`
- Modify: `tests/Trailblazer.Tests/Pathing/Map/NavigationMapBuilderTests.cs`
- Modify: `tests/Trailblazer.Tests/Pathing/Map/NavigationMapTokenImporterTests.cs`
- Add: `tests/Trailblazer.Tests/Pathing/Map/NavigationMapDefaultAndTransitionRuleTests.cs`

- [x] RED absent/default/explicit/overlay precedence on dense and sparse maps; pin default coverage to physically present cells inside normalized `GridBinding`.
- [x] RED complete replacement semantics: no field merge; `Suppress` tombstones every lower layer; `RevertToBake` falls through baked -> default -> none.
- [x] RED Gas-default map replacement by Liquid-default map and exact malformed/default/rule transactional rejection.
- [x] RED rule validation for exact medium values, SameCell/PositiveFaceContact, capability flags, nonnegative `ActionCost`, locomotion hints, stable ID, duplicate IDs, canonical ordering, checked retained bytes, exact configured capacity, and one-over transactional rejection.
- [x] Stage positive finite rule ceilings internally in `NavigationOperationLimits`, derived from the existing transition per-map/total ceilings so no public constructor/property changes in this preparatory commit. Include canonical rule arrays in prepared-map/candidate retained-byte accounting and never partially publish an over-limit candidate. Task 10 replaces the temporary derivation with required public `MaxTransitionRulesPerMap`/`MaxTransitionRules` arguments and properties atomically with the final rule API.
- [x] Implement internal optional complete `NavigationCell? DefaultCell` and internal canonical `TraversalTransitionRule[]` storage in `NavigationMap`/prepared map. Keep the new authoring members/types internal until Task 10 exposes the one final public suite.
- [x] Keep `TraversalTransitionDefinition.AdditionalCost` unchanged during preparation. Task 10 performs the public clean rename to `ActionCost` across every direct reader/named constructor call; the distinct physical `NavigationConnection.AdditionalCost` is never renamed.
- [x] Keep one sorted rule array; do not add rule buckets/indexes.
- [x] Tag explicit/rule identity by kind; reject duplicates only within definition owner or rule array.
- [x] Update importer/builder XML docs and exact equality/hash/API snapshot expectations.
- [x] Run focused map/import/overlay/API Release + ReleaseLean gates and diff checks.
- [x] Review and commit: `feat(pathing): author medium defaults and transition rules`.

## Task 3: Compose Medium Structural State And Volume Anchors

**Files:**

- Add: `src/Trailblazer/Pathing/Graph/NavigationMediumStateRef.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationGraphCellState.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationNodeState.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationMapInstance.ComposeWork.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationWorldGraph.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationSurfaceComponentKey.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationSurfaceComponentBuildWork.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationSurfaceComponentIndex.cs`
- Modify only if composition needs it: `src/Trailblazer/Pathing/Search/NavigationDependencyWorkspace.cs`
- Add: `tests/Trailblazer.Tests/Pathing/Graph/NavigationMediumGraphTests.cs`
- Modify: `tests/Trailblazer.Tests/Pathing/Graph/NavigationMapInstanceTestFactory.cs`

- [x] RED effective default composition, multi-media cell states, no-cell absence, overlay media replacement, and affected/unaffected dependency pages.
- [x] RED medium structural components for rectangular 6 and hex 8 positive faces; prove different components reject but same component does not bypass profile/policy/passability.
- [x] RED free-flight volume foot-anchor derivation centered in the GridForge-issued cell volume for several valid body heights, pointy/flat hex, anisotropic rectangular metrics, and invalid or unrepresentable arithmetic. Task 5 exclusively owns multi-cell/cross-grid placement, swept-union fit, semantic witness, and too-wide/too-tall unavailability proofs.
- [x] Implement compact `(NavigationNodeRef, TraversalMedium)` identity without eagerly materializing three graph nodes.
- [x] Keep structural components keyed only by medium presence and positive-face contact; do not include profile, policy, clearance, shortcuts, or semantic transitions.
- [x] Record default/media/component changes through existing page/component dependency stamps; no second version clock.
- [x] Run focused composition/component/publication Release + ReleaseLean gates, review, and commit: `feat(pathing): compose medium graph states`.

## Task 4: Publish Transition Pages And The Canonical Rule Table

**Files:**

- Add: `src/Trailblazer/Pathing/Graph/NavigationTransitionPage.cs`
- Add: `src/Trailblazer/Pathing/Graph/NavigationTransitionRuleTable.cs`
- Add: `src/Trailblazer/Pathing/Graph/NavigationTransitionRefreshWork.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationMapInstance.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationWorldGraph.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationWorldGraph.StructuralPreparationWork.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationMaterializedComponentWork.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationGraphRuntime.cs`
- Modify: `src/Trailblazer/Pathing/Graph/GraphDependencyStamp.cs`
- Modify: `src/Trailblazer/Pathing/Map/Operations/NavigationOperationCandidate.cs`
- Modify: `src/Trailblazer/Pathing/Map/Operations/NavigationOperationCandidate.MapFold.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationDependencyStampWork.cs`
- Modify accounting capacity defaults only: `src/Trailblazer/Runtime/TrailblazerWorldContextSettings.cs`
- Add: `tests/Trailblazer.Tests/Pathing/Graph/NavigationTransitionPublicationTests.cs`
- Modify accounting/capacity/hash expectations only: `tests/Trailblazer.Tests/Pathing/Graph/NavigationWorldGraphStoreTests.cs`, `NavigationStructuralCompositionCarryoverTests.cs`, `NavigationMediumGraphTests.cs`, `NavigationMapStateOwnershipTests.cs`, `NavigationGraphCapacityTests.cs`, `NavigationSurfaceAStarTests.cs`, `NavigationFlowFieldTests.cs`, `Phase5GraphFlowDeterminismMatrixTests.cs`, and `tests/Trailblazer.Tests/Runtime/TrailblazerWorldContextSettingsTests.cs`

- [x] RED explicit baked and overlay `Upsert`/`Suppress`/`RevertToBake` composition into immutable source-outgoing and destination-incoming transition pages, including same-medium definitions.
- [x] RED inactive endpoints/media, exact explicit outgoing/incoming ordering, canonical ID-first rule-table ordering/reuse, forward/reverse parity, duplicate-owner rejection, and one-over rule/page/candidate byte capacities. Defer merged definition/rule tagged identity and tie ordering to Task 6.
- [x] RED affected/unaffected cell/default/rule/transition changes, transactional rejection, exact page/component dependency stamps, retained-byte accounting, and current/retired graph lease drain.
- [x] Publish one canonical bounded rule table and source-owned transition pages in the same candidate graph transaction as effective cells; do not retain the old registry/query cache or add a separately named transition cache/index.
- [x] Keep the new table/pages and their enumeration entry points internal until Task 10; Task 6 consumes them directly.
- [x] Run publication/overlay/store/concurrency Release + ReleaseLean gates, reviews, and commit: `feat(pathing): publish transition graph pages`.

## Task 5: Prepare Exact Medium Admission Internals

**Files:**

- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationResolvedPathQuery.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationEndpointResolutionWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationQueryAdmissionWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarQueryWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowQueryWork.cs`
- Add: `src/Trailblazer/Pathing/Graph/NavigationVolumeAnchorEvaluator.cs`
- Modify: `src/Trailblazer/Pathing/Graph/TraversalEvaluator.cs`
- Modify: `src/Trailblazer/Pathing/Map/NavigationCell.cs`
- Modify: `src/Trailblazer/Pathing/Query/NavigationAgentProfile.cs`
- Modify: `src/Trailblazer/Pathing/Search/Ray/NavigationRayWorkspace.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldPayloadKey.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationSurfaceAStarWork.cs`
- Modify: `tests/Trailblazer.Tests/Pathing/Graph/NavigationEndpointResolutionTests.cs`
- Modify: internal graph query test factories only
- Modify: internal benchmark query factories only

- [x] RED internal resolved Solid/Gas/Liquid start, nonempty known target-media mask, agent-subset validation, `Unknown` rejection, and transition-disabled mismatch -> NoPath.
- [x] RED endpoint candidates filtered before Strict/NearestNavigable ranking: exact start; disabled targets by StartMedium only; enabled targets by any requested medium. Retain all qualifying media at the one winning physical address plus one deterministic resolution medium/anchor and medium/address tie order.
- [x] RED Strict and NearestNavigable Gas/Liquid endpoints, including a zero-length route and large multi-cell/cross-grid bodies. A candidate is unavailable when any positive-overlap placement prism is physically missing, wrong-medium, capability-blocked, policy-blocked, or lacks clearance; pin exact existing graph-page dependencies (record dependency-only mapped cells before skipping semantics, otherwise stale), world-stamp currentness, exact capacity, and exact budget one-below.
- [x] Implement one internal degenerate-union `NavigationVolumeAnchorEvaluator` used by endpoint resolution and reused by Task 6 face/shortcut evaluation; do not let the endpoint cell alone certify placement.
- [x] Extend the existing `NavigationRayWorkspace` here with both caller-owned GridForge requirements: `SwiftList<GridNavigationBodyTraceCell>` and `GridNavigationBodyTraceScratch`, sized from the existing ray capacities. Admission, dispatcher, and ray work reuse this one storage owner with no per-call allocation.
- [x] RED disabled single-anchor floor heuristic and enabled zero A* heuristic. Prepare the Flow payload key with exact medium identity, but defer actual zero-cost multi-target medium seeds and Flow medium-state workspace to Task 9.
- [x] Extend the existing internal resolved-query/admission authority with exact medium fields; do not add a second query DTO. Keep the current public `TraversalIntent`/`PathQuery` surface and its existing projection unchanged until Task 10.
- [x] Do not modify or port `GuidedVolumeExitPlanner`, Hybrid types, Navigator, public guide wrappers, records, or benchmarks in this preparatory task.
- [x] Run internal query/admission/cache Release + ReleaseLean gates, reviews, and commit: `feat(pathing): prepare medium query admission`.

## Task 6: Add One Canonical Medium Edge Dispatcher

**Files:**

- Add: `src/Trailblazer/Pathing/Graph/NavigationTraversalEdgeEnumerator.cs`
- Add: `src/Trailblazer/Pathing/Graph/NavigationIncomingTraversalEdgeEnumerator.cs`
- Add: `src/Trailblazer/Pathing/Graph/NavigationVolumeEdgeEvaluator.cs`
- Add: `src/Trailblazer/Pathing/Graph/NavigationTransitionEdgeEvaluator.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationWorldGraph.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationWorldGraph.NativeEdges.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationTransitionPage.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationVolumeAnchorEvaluator.cs`
- Modify: `src/Trailblazer/Pathing/Graph/TraversalEvaluator.cs`
- Modify: `src/Trailblazer/Pathing/Search/NavigationDependencyWorkspace.cs`
- Modify: `src/Trailblazer/Pathing/Query/NavigationWorkMeter.cs`
- Add: `tests/Trailblazer.Tests/Pathing/Graph/NavigationVolumeEdgeTests.cs`
- Add: `tests/Trailblazer.Tests/Pathing/Graph/NavigationTransitionEdgeTests.cs`

- [x] RED unchanged Solid native/seam/explicit behavior through the dispatcher.
- [x] RED Gas/Liquid positive-face movement using resolved volume anchors plus existing GridForge portal/traversal/body-segment fast path without step/drop checks, and covered-prism union fallback for multi-cell/cross-grid bodies. Semantic `PositiveFaceContact` requires the actual agent profile to fit its directed portal; same-medium/override volume action legs retain the shared swept-union authority.
- [x] RED rectangular 2-axis/3-axis and pointy/flat hex shortcuts through GridForge's complete direction sets and swept-body operation; missing/wrong-medium/blocked/forbidden/large-body witnesses reject and record dependencies.
- [x] RED exact Fixed64 shortcut costs: anisotropic sqrt2/sqrt3 geometry with `TryCeiling`; target cell/area enter cost once; witness enter cost zero.
- [x] RED same-medium Jump/Climb plus medium-changing anchored/rule actions, SameCell medium-anchor points, PositiveFaceContact directed contact points, explicit point-override leg validation, forward/reverse canonical ordinals, capabilities, policy, locomotion hints, teleporter `ActionCost`, and zero heuristic when transitions are enabled.
- [x] Implement one dispatcher that delegates to existing surface, stateless volume, or transition evaluation; retain yielding state only for the case that needs it. Base-family order remains unchanged; semantic definitions/rules then use destination/medium/type/tag/ID order, and incoming traversal replays the predecessor's forward dispatcher to recover the exact ordinal.
- [x] Meter base edges, shortcut candidates, swept coverage, union checks, rule rows, each procedural primary/seam contact, transition checks, and dependency merges exactly once; long seam rows resume through the existing cursor with no unmetered scan.
- [x] Keep query-time volume candidate count <=20 rect / <=12 hex; no per-node generated edge storage, recursive ray, topology formulas, or per-edge allocation.
- [x] Reuse both GridForge covered-cell result/scratch objects already owned by the A*/Flow slot's existing `NavigationRayWorkspace`; do not add a volume-shortcut workspace, duplicate buffer/scratch, capacity family, or pool.
- [x] Run evaluator/geometry/work-meter Release + ReleaseLean gates, warmed allocation checks, reviews, and commit: `feat(pathing): evaluate unified medium edges`.

## Task 7: Activate Volume-Aware Navigation Rays

**Files:**

- Modify: `src/Trailblazer/Pathing/Search/Ray/NavigationRayWork.cs`
- Modify (contains `NavigationRayRequest`): `src/Trailblazer/Pathing/Search/Ray/NavigationRayStatus.cs`
- Modify only through the Task 6 dispatcher/evaluator authority: `src/Trailblazer/Pathing/Graph/NavigationVolumeEdgeEvaluator.cs`
- Modify: `tests/Trailblazer.Tests/Pathing/Graph/NavigationRayTests.cs`
- Add: `tests/Trailblazer.Tests/Pathing/Graph/NavigationVolumeRayTests.cs`
- Update exact-medium internal ray request call sites only: `src/Trailblazer/Pathing/Search/AStar/NavigationEndpointResolutionWork.cs`, `src/Trailblazer/Pathing/Search/AStar/NavigationSurfaceAStarWork.cs`, `src/Trailblazer/Pathing/Search/Flow/NavigationSelectedEdgeProgressWork.cs`, `src/Trailblazer/Pathing/Search/Guide/TrailblazerGuideService.cs`, and matching ray tests/benchmarks.

- [x] RED Gas/Liquid one-prism positive-face chains plus multi-cell/cross-grid covered-prism union fallback for rectangular, pointy hex, and flat hex worlds.
- [x] RED exact source/target medium identity, policy/capability/clearance rejection, missing physical evidence, affected/unaffected dependencies, stale world/publication, exact work/capacity one-below, and warmed zero allocation.
- [x] RED same-medium-only success and explicit refusal to cross or skip a semantic transition; volume shortcuts may be evaluated only through the Task 6 dispatcher and may not recursively invoke the ray.
- [x] Reuse the typed covered-cell result/scratch already added to `NavigationRayWorkspace` by Task 5 and the canonical dispatcher/union API; do not add a second buffer, scratch, ray, volume workspace, topology formula, or geometry helper.
- [x] Keep current public direct-heading behavior unchanged. Task 8 consumes this internal ray for A* simplification; Task 10 wires the final controller/public behavior and deletes the old direct-volume check.
- [x] Run ray/evaluator/GridForge integration Release + ReleaseLean gates, stale/allocation gates, reviews, and commit: `feat(pathing): trace volume navigation rays`.

## Task 8: Extend A* To Medium States And Actionable Guide Steps

**Files:**

- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarNodeTable.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarWorkspace.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationSurfaceAStarWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarPayload.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarPayloadKey.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarQueryWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarGuidePoint.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarGuideLease.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationTraversalEdgeEnumerator.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationIncomingTraversalEdgeEnumerator.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationSurfaceEdgeRouteWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/Ray/NavigationRayWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarAdmissionGate.cs`
- Modify: `src/Trailblazer/Pathing/Query/NavigationQueryLimits.cs`
- Defer public wrapper cutover: `src/Trailblazer/Pathing/Search/AStar/NavigationGuideLease.cs`
- Add: `src/Trailblazer/Pathing/Search/Guide/NavigationGuideStep.cs`
- Add: `src/Trailblazer/Pathing/Search/Guide/NavigationTransitionInstruction.cs`
- Modify: `tests/Trailblazer.Tests/Pathing/Graph/NavigationSurfaceAStarTests.cs`
- Modify: A*/dispatcher/ray compatibility tests for the direct medium-state and
  split connection-step signature cutovers
- Modify: A* admission/concurrency, runtime default-limit, and benchmark
  accounting call sites for the exact transition-payload bound
- Add: `tests/Trailblazer.Tests/Pathing/Graph/NavigationTransitionGuideTests.cs`

- [x] RED A* Gas/Liquid open face/shortcut paths, multiple target media, mixed same/change-medium actions, exact costs, canonical ties, and blocked face fallback.
- [x] RED the inner `NavigationAStarGuideLease.TryGetCurrentStep` movement and transition results. Keep the public wrapper unchanged until Task 10's atomic consumer cutover; do not add a temporary public overload or forwarding facade.
- [x] RED held action, zero movement advancement while pending, exact completion, wrong instruction, copied lease, double completion, remove/re-add ABA, stale mutation, and disposal.
- [x] Store transition payload only for transition guide entries. Keep cached payload immutable; stamp only the existing lease-acquisition generation plus step ordinal and keep current medium per acquired lease. Existing lease/dependency validation remains the publication-staleness authority.
- [x] Prevent simplification from crossing a semantic transition; reuse the same volume-aware ray only for same-medium subsequences.
- [x] Update cache byte reservations/accounting with exact `Unsafe.SizeOf` values and exact capacity one-below tests.
- [x] Run A*/guide/cache/concurrency/allocation Release + ReleaseLean gates, reviews, and commit: `feat(pathing): guide astar through medium states`.

## Task 9: Extend Flow To The Same Medium/Transition Authority

**Files:**

- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldNode.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldOpenHeap.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldWorkspace.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldPayload.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldPayloadCache.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowQueryWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowAdmissionGate.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldGuideLease.cs`
- Modify only the existing public wrapper's internal meter projection while deferring its public cutover: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldLease.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationSelectedEdgeProgressWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/GuideSampleWorkMeter.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/GuideSampleBatch.cs`
- Add: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowSample.cs`
- Modify only as required for shared dispatcher/world/dependency/sample metering: `src/Trailblazer/Pathing/Graph/NavigationIncomingSurfaceEdgeEnumerator.cs`, `NavigationIncomingTraversalEdgeEnumerator.cs`, `NavigationSelectedEdgeRef.cs`, `NavigationVolumeAnchorEvaluator.cs`, `src/Trailblazer/Pathing/Query/NavigationWorkMeter.cs`, `src/Trailblazer/Pathing/Search/Ray/NavigationRayWork.cs`, `src/Trailblazer/Pathing/Search/Guide/NavigationTransitionInstruction.cs`, `TrailblazerGuideService.cs`, and `src/Trailblazer/Pathing/Search/AStar/NavigationAStarGuideLease.cs`
- Modify the shared endpoint-to-search raw-world handoff only as required to preserve one admission baseline across A* and Flow: `src/Trailblazer/Pathing/Search/AStar/NavigationEndpointResolutionWork.cs`, `NavigationQueryAdmissionWork.cs`, `NavigationResolvedPathQuery.cs`, `NavigationAStarQueryWork.cs`, and `NavigationSurfaceAStarWork.cs`.
- Modify matching Flow/transition/cache/admission/concurrency/equivalence/determinism/architecture tests and Flow benchmark construction only; do not widen public API/controller/serialization coverage before Task 10.

- [x] RED reverse incoming native/shortcut/explicit/rule traversal parity with A*, including multi-target seeds and exact canonical selected action.
- [x] RED the inner `NavigationFlowFieldGuideLease.TrySample(... out NavigationFlowSample)` ordinary heading/target/medium and transition instruction. Keep the public wrapper unchanged until Task 10's atomic consumer cutover; do not add a temporary public overload or forwarding facade.
- [x] RED per-lease pending action/current medium, explicit completion, same-lease rejoin constrained to selected medium/action, stale/capacity/budget/cost propagation, copied/double-dispose/ABA, and zero warm allocation.
- [x] Merge every blocked shortcut/rule dependency into reusable NoPath proof before workspace reset.
- [x] Update batch sampling to the new result directly; no translation facade.
- [x] Run Flow/A* parity/cache/guide/concurrency Release + ReleaseLean gates, reviews, and commit: `feat(pathing): guide flow through medium states`.

## Task 10: Atomically Cut Public APIs, Controllers, Serialization, And Legacy Providers

**Files:**

- Delete: `src/Trailblazer/Pathing/Traversal/TraversalDomain.cs`
- Modify: `src/Trailblazer/Pathing/Traversal/TraversalIntent.cs`
- Modify: `src/Trailblazer/Pathing/Query/PathQuery.cs`
- Modify: `src/Trailblazer/Pathing/Map/NavigationMap.cs`
- Modify: `src/Trailblazer/Pathing/Map/NavigationMapBuilder.cs`
- Modify: `src/Trailblazer/Pathing/Map/TraversalTransitionDefinition.cs`
- Make final public: `src/Trailblazer/Pathing/Map/TraversalTransitionRule.cs`, `TraversalTransitionRuleScope.cs`, and `src/Trailblazer/Pathing/Transition/TraversalTransitionLocomotionHints.cs`
- Make final public with XML/API snapshot coverage: `src/Trailblazer/Pathing/Search/Guide/NavigationGuideStep.cs`, `NavigationTransitionInstruction.cs`, and `src/Trailblazer/Pathing/Search/Flow/NavigationFlowSample.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationGuideLease.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldLease.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/GuideSampleBatch.cs`
- Modify: `src/Trailblazer/Pathing/Search/Guide/TrailblazerGuideService.cs`
- Modify: `src/Trailblazer/Navigation/Steering/NavSteering.cs`
- Modify: `src/Trailblazer/Navigation/Steering/NavSteering.Requests.cs`
- Modify: `src/Trailblazer/Navigation/Steering/NavSteering.Simulation.cs`
- Modify: `src/Trailblazer/Navigation/Steering/NavSteering.Serialization.cs`
- Modify: `src/Trailblazer/Navigation/Navigator/Navigator.cs`
- Modify: `src/Trailblazer/Navigation/Navigator/Navigator.Serialization.cs`
- Delete after direct instruction migration: `src/Trailblazer/Navigation/Navigator/Guidance/NavigatorGuidedTraversalState.cs` and `GuidedClimbIntentMode.cs`
- Modify: `src/Trailblazer/Navigation/Steering/Serialization/PathQueryRecord.cs`
- Add: `src/Trailblazer/Navigation/Steering/Serialization/NavigatorPathSessionRecord.cs`
- Add final public identity discriminator: `src/Trailblazer/Pathing/Search/Guide/NavigationTransitionIdentityKind.cs`
- Delete: `src/Trailblazer/Navigation/Steering/Serialization/PathRequestRecord.cs`
- Modify: `tests/Trailblazer.Tests/Navigation/Steering/NavSteering.Tests.cs`
- Modify: `tests/Trailblazer.Tests/Navigation/Navigator/Navigator.Tests.cs`
- Modify: `tests/Trailblazer.Tests/Navigation/Navigator/NavigatorSerialization.Tests.cs`
- Modify: all current query/rule/guide consumers under source, tests, and benchmarks found by exact `rg`
- Delete: `src/Trailblazer/Pathing/Search/Volume/**`
- Delete: `src/Trailblazer/Pathing/Search/Hybrid/**`
- Delete: `src/Trailblazer/Pathing/Search/VoxelResolution/VolumeVoxelFinder.cs`
- Delete: `src/Trailblazer/Pathing/VolumeRules/**`
- Delete: `src/Trailblazer/Pathing/Transition/Registry/**`
- Delete: `src/Trailblazer/Pathing/Transition/Query/**`
- Delete: `src/Trailblazer/Pathing/Transition/TrailblazerTransitionService.cs`
- Delete: `src/Trailblazer/Pathing/Transition/TraversalTransition.cs`
- Delete: `src/Trailblazer/Pathing/Transition/TraversalTransitionAnchor.cs`
- Delete after migrating every direct authoring/PathManager consumer in this task: `src/Trailblazer/Pathing/Transition/Generation/GeneratedTraversalTransitionBuilder.cs`
- Delete the superseded chart-era authoring lane: `src/Trailblazer/Pathing/Authoring/TraversalAuthoringMap.cs`, `TraversalBuildResult.cs`, `ParsedTraversalCell.cs`, `TraversalLegend.cs`, `TraversalLegendEntry.cs`, and `src/Trailblazer/Pathing/Diagnostics/TraversalAuthoringMap.Extensions.cs`
- Delete corresponding `PathManager` and `TrailblazerPathingService` overloads plus authoring/diagnostic tests; `NavigationMapTokenImporter` is the retained immutable import authority
- Modify: `src/Trailblazer/Pathing/PathManager.cs`
- Modify: `src/Trailblazer/Pathing/PathingWorldState.cs`
- Modify: `src/Trailblazer/Runtime/TrailblazerWorldContext.cs`
- Delete exact legacy request files: `IPathRequest.cs`, `PathRequestCacheKey.cs`, `PathRequestHashBuilder.cs`; retain or relocate the live `PathRequestContextResolver.cs`
- Delete when their final consumer is gone: `src/Trailblazer/Pathing/Search/Survey/**`, `IGuide.cs`, `IWaypointGuide.cs`, `GuidePool.cs`, `PathGuideFactory.cs`, `TrailblazerGuideState.cs`, `src/Trailblazer/Pathing/Search/OpenSet/**`, `AStarWaypoint.cs`, and volume `HeuristicMethod.cs`
- Delete: `src/Trailblazer/Navigation/Navigator/Guidance/VolumeExit/**`
- Delete corresponding tests, `tests/Trailblazer.Benchmarks/Pathing/VolumePathRequestBenchmarks.cs`, and the obsolete `PathHeapBenchmarks.cs` benchmark/README registration with `Search/OpenSet/**`
- Modify: `tests/Trailblazer.Tests/Phase0/PublicApiSnapshot.txt`

- [x] RED the final public exact Solid/Gas/Liquid `StartMedium`, known nonempty `TargetMedia`, agent subset/Unknown rejection, and transition-disabled mismatch. Change `TraversalIntent`/`PathQuery`, migrate every source/test/benchmark reader returned by `rg "StartDomain|TargetDomain|CurrentMedium|TraversalDomain"`, then delete `TraversalDomain` with zero residue.
- [x] RED public map default/rule authoring and atomically expose the Task 2 types/members. Add final authored locomotion hints to explicit definitions, replacing Task 4's staged `None`; never infer them from `TraversalTransitionType`. Rename `TraversalTransitionDefinition.AdditionalCost` to `ActionCost` across every direct reader/named constructor with no alias; preserve physical `NavigationConnection.AdditionalCost`.
- [x] Replace Task 2's internal rule-limit derivation with required public `maxTransitionRulesPerMap`/`maxTransitionRules` constructor arguments and `MaxTransitionRulesPerMap`/`MaxTransitionRules` properties; migrate every settings/test/benchmark caller in the same atomic cut.
- [x] RED exact current-frame/start-medium mismatch before guide acquisition; no silent volume-first query synthesis.
- [x] RED movement step steering, transition pending exposure, zero ordinary heading while held, built-in locomotion hints, exact `CompletePendingTransition`, cancel/retry, stale action, StopMove/Arrive/load/dispose ownership.
- [x] RED standalone query exact JSON/MemoryPack round-trip and Navigator session load that rebuilds start position/medium from host-restored state while retaining durable destination/profile/policy/algorithm/budget/target-media intent.
- [x] RED malformed/missing/old Volume/Hybrid/handoff/guide-cursor wire shapes in both transports; preserve existing shell transactionally.
- [x] Atomically expose only public `TryGetCurrentStep`/`NavigationFlowSample`, migrate all guide-service, NavSteering, batch, test, benchmark, and serialization consumers, then delete `TryGetCurrentWaypoint` and heading-only `TrySample`. No intermediate commit may expose both public suites.
- [x] RED and expose `CompletePendingTransition(in NavigationTransitionInstruction)` on both public `NavigationGuideLease` and `NavigationFlowFieldLease`, with mismatch, stale, copied-lease, and double-completion failure that leaves cursor state unchanged; cover XML docs and public API fingerprints.
- [x] Remove automatic guided-volume handoff activation and every `_hybridRouteGuide`, `_volumeGuide`, `_currentRequest`, unit-size, range, heuristic, waypoint-index, and old-mode field after direct migration.
- [x] Keep one Navigator pending instruction field only; the lease remains cursor/action owner.
- [x] Build all production/test/benchmark projects and enumerate the real final owners in `PathManager`, `PathingWorldState`, `TrailblazerWorldContext`, `TrailblazerTransitionService`, and `TrailblazerGuideService`; migrate/delete each owner before its provider. Do not port `GuidedVolumeExitPlanner` or any Hybrid carrier to the final query.
- [x] Delete the complete duplicate `TraversalAuthoringMap`/`TraversalBuildResult`/legend/parser/diagnostic-extension lane and its `PathManager`/`TrailblazerPathingService` overloads/tests in the same compile-clean cut; only then delete `GeneratedTraversalTransitionBuilder`. Do not port this chart-era authoring lane beside `NavigationMapTokenImporter`.
- [x] Delete the legacy families atomically with the public cutover; preserve no alias, forwarding constructor, compatibility factory, dormant discriminator, old serialized key, or historical API snapshot entry.
- [x] Add source-architecture tests proving exactly one query/guide/cache/publication authority and zero exact retired source/test/benchmark/API/JSON/MemoryPack tokens. Task 12 owns the final docs-zero gate.
- [x] Run clean multi-target Release + ReleaseLean builds, full direct tests, Navigator/NavSteering/serialization/public API gates, reviews, and one atomic commit: `refactor(pathing): cut over unified medium routing`.

## Task 11: Prove Ladder And Duck Simulation Scenarios

**Files:**

- Add: `tests/Trailblazer.Tests/Navigation/Phase7TransitionSimulationTests.cs`
- Add: `tests/Trailblazer.Tests/Pathing/Graph/NavigationTransitionRuleTests.cs`
- Modify: `src/Trailblazer/Pathing/Map/NavigationMapTokenImporter.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarGuideLease.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarQueryWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationSelectedEdgeProgressWork.cs`
- Modify: `src/Trailblazer/Navigation/Steering/NavSteering.cs`
- Modify: `src/Trailblazer/Navigation/Navigator/Navigator.cs`
- Modify: `tests/Trailblazer.Tests/Support/GuidedPathTestScene.cs`
- Modify: matching focused regression tests under `tests/Trailblazer.Tests`

- [x] RED ladder: no route, overlay-drop a bidirectional ladder from Solid cliff into Liquid, acquire held Climb instruction, complete exactly, continue same guide, move/remove ladder, stale old instruction/lease, reacquire NoPath, drain resources.
- [x] RED ladder locomotion hints for ordinary Climb and shoreline SwimExit request/preserve behavior.
- [x] RED duck: one public Liquid->Gas PositiveFaceContact Takeoff rule serves multiple water-surface cells; Swim|Fly succeeds from each, otherwise-equivalent non-Fly agent fails, completion continues same A*/Flow guide.
- [x] RED SameCell takeoff, same-medium Jump/Climb, distant cheap teleporter, rule mutation, cell flood/drain, affected/unaffected cache reuse, and exact dependency invalidation.
- [x] Use only public map/overlay/query/guide/Navigator APIs in the showcase tests; no internal fixture shortcut for the behavior under demonstration.
- [x] Run focused scenarios in Release + ReleaseLean, reviews, and commit: `test(navigation): prove dynamic transition simulations`.

## Task 12: Refresh Public Documentation And Decide Navigation-Ray Visibility

**Files:**

- Modify: `README.md`
- Modify: `docs/wiki/Overview.md`
- Modify: `docs/wiki/Home.md`
- Modify: `docs/wiki/Pathing.md`
- Modify: `docs/wiki/NavigationCharts.md`
- Modify: `docs/wiki/ChartAuthoring.md`
- Modify: `docs/wiki/PathManager.md`
- Modify: `docs/wiki/VolumeTraversal.md`
- Modify: `docs/wiki/Transitions.md`
- Modify: `docs/wiki/PathGuides.md`
- Modify: `docs/wiki/NavSteering.md`
- Modify: `docs/wiki/Navigator.md`
- Modify: `docs/wiki/Serialization.md`
- Modify: `docs/feature-work/gridTopologyNavigationMapRefactorPlan.md`

- [x] Replace all chart/partition/predicate/VolumePathRequest/HybridRoute/registry/handoff documentation with map defaults, unified medium-state query, explicit/rule actions, and completion.
- [x] Document host materialization of prior predicate/terrain results into defaults/entries/overlays; terrain remains optional and is not Volume truth.
- [x] Include concise ladder and duck public-API examples and the Gas-default -> Liquid-default flooding replacement.
- [x] Evaluate the now-proven ray against surface and volume consumers. Promote only one clean public query/result API if it can hide meters/workspaces/constraints and is genuinely useful; otherwise record the explicit internal-specialization decision in the tracker. Do not add a facade merely to close the ledger.
- [x] Run docs/API exact-token scans and link checks; review and commit: `docs(pathing): document unified medium routing`.

## Task 13: Determinism, Benchmarks, Coverage, Packaging, And Exit Reviews

**Files:**

- Add: `tests/Trailblazer.Tests/Pathing/Graph/Phase7VolumeTransitionDeterminismMatrixTests.cs`
- Add: `tests/Trailblazer.Benchmarks/Pathing/NavigationVolumeRoutingBenchmarks.cs`
- Modify: `tests/Trailblazer.Benchmarks/README.md`
- Modify: `src/Trailblazer/Pathing/Query/NavigationWorkMeter.cs`, canonical
  volume-candidate/union/dependency-merge call sites, and their matching graph
  tests for the four internal observation scalars required by this gate
- Modify: `NavigationFlowFieldGuideTests.cs` for the one coverage-justified
  destination-recovery fact; delete the zero-caller overlay/media/baseline
  helper shapes from `NavigationOperationProcessor.cs`,
  `NavigationMapInstance.cs`, `NavigationGridBaselineCapture.cs`, and
  `NavigationBaselineRebuild.cs`
- Modify: `NavigationContractArchitecture.Tests.cs` to enforce the complete
  exact retired-identifier and wire-key residue set across active source,
  tests, benchmarks, README/wiki documentation, and the public API snapshot
- Modify: `docs/feature-work/gridTopologyNavigationMapRefactorPlan.md`

- [x] RED a canonical digest matrix covering rectangular 2-axis/3-axis, anisotropic, pointy/flat hex vertical diagonals, Gas/Liquid, A*/Flow, same/change-medium actions, ladder, duck, teleporter, flood mutation, serialization, and NoPath dependencies.
- [x] Check in exact culture-invariant hashes, then run Debug, Release, ten additional Release processes, and direct ReleaseLean serially; require byte-identical sorted digests.
- [x] Add benchmark scenarios for open/obstructed rect 2D/3D, large-body coverage, hex vertical diagonal, A*/Flow, rule scan, ladder action, duck takeoff, mixed route, and cache hits.
- [x] Report settled medium states, evaluated edges, primary and shortcut volume candidates, covered voxel intervals, union checks, rule/transition candidates, successful dependency merges separately from emitted dependency facts, guide steps, p50/p95/p99/max, and allocation. Warm guide sampling must be 0 B; immutable payload allocation is reported, not hidden.
- [x] Compare full 26/20 routing with face-only control; open diagonal cases must settle no more states. Add an internal compiled GridForge optimization only if measured stateless coverage dominates and the same reviewers approve it.
- [x] Run project-wide coverage/CRAP analysis once; add focused tests only for new high-risk Phase 7 gaps, not unrelated historical debt.
- [x] Run serial final gates: restore; four direct source TFM/config builds; full Release; full direct ReleaseLean; benchmark build/list/smoke/canonical; JSON/MemoryPack serialization; public API snapshot; package and package-content checks; `git diff --check`; exact forbidden-residue and allocation scans. Pack GridForge + GridForge.Lean into the plan's isolated feed, restore Trailblazer in normal package mode from that feed without sibling project references/global-feed mutation, and prove the same source/tests compile against the exact packed API.
- [x] Freeze the complete range and request independent full correctness and ponytail reviews. Fix every P0-P2 RED-first, rerun affected focused/full gates, and obtain scoped approval.
- [x] Mark the Phase 7 tracker rows complete with exact commits/evidence and commit: `test(pathing): close phase 7 unified routing`.

## Required Final Residue Gate

The following exact retired authorities must have zero active source/test/benchmark/docs/API/serialization occurrences except explicit negative architecture assertions and historical completed-plan evidence:

`TraversalDomain`, `IPathRequest`, `IGuide`, `IWaypointGuide`, `GuidePool`, `VolumePathRequest`, `VolumeSurveyor`, `VolumeSurveyResult`, `VolumeGuide`, `VolumeVoxelFinder`, `TrailblazerWorldContext.VolumeRules`, `VolumeVoxelRule`, `VolumeMediumRules`, `VolumeMediumRulesState`, `TrailblazerVolumeRulesService`, `HybridPathRequest`, `HybridRoutePlanner`, `HybridRoutePlan`, `HybridRouteStep`, `HybridRouteGuide`, `GuidedVolumeExitPlanner`, `GuidedVolumeExitHandoff`, `TrailblazerTransitionService`, `TraversalTransition`, `TraversalTransitionAnchor`, `TraversalTransitionRegistry`, `TraversalTransitionRegistryState`, `TraversalTransitionQuery`, `TraversalTransitionQueryCache`, `GeneratedTraversalTransitionBuilder`, `TraversalAuthoringMap`, `TraversalBuildResult`, `ParsedTraversalCell`, `TraversalLegend`, `TraversalLegendEntry`, `TraversalAuthoringMapExtensions`, `ReusableSurveyResultCache`, `PathGuideFactory`, `TrailblazerGuideState`, `PathRequestRecord`, `TryGetCurrentWaypoint`, heading-only `TrySample`, old Volume/Hybrid wire modes, `UnitSize`, `AllowUnwalkableEndpoints`, `MaxPathSearchRange`, and volume `HeuristicMethod`.
