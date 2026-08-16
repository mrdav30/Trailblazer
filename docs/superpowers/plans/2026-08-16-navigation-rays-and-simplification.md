# Phase 6 Navigation Rays And Simplification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace legacy surface line-of-sight and Flow recovery-A* behavior with one bounded deterministic navigation-ray authority, then use it to emit portal-correct simplified A* guides and same-lease Flow rejoin headings.

**Architecture:** FixedMathSharp supplies one exact segment-separation predicate. GridForge owns bounded ordered trace collection plus one shared point/segment body-clearance and portal-crossing authority. Trailblazer maps those exact intervals onto immutable graph edges, meters all semantic work, uses the ray during endpoint resolution and A* payload construction, and shares one blocking context-owned immediate-ray workspace between steering and exceptional Flow rejoin.

**Tech Stack:** C# 11, `Fixed64`/`Vector2d`/`Vector3d`, FixedMathSharp.Geometry, GridForge, SwiftCollections, xUnit v3, FluentAssertions, BenchmarkDotNet, `netstandard2.1`, `net8.0`.

## Global Constraints

- Determinism, correctness, maintainability, then performance—in that order.
- No `float`, `double`, `System.Numerics`, wall-clock cutoff, randomness, LINQ, recursion, or unordered collection authority in simulation paths.
- FixedMathSharp owns general segment/capsule/convex math; GridForge owns topology/prisms/portal certificates; Trailblazer owns navigation semantics and budgets.
- Production changes follow strict RED → observed failure → minimal GREEN → refactor → fresh GREEN.
- Every warm direct ray, A* workspace reuse, Flow rejoin sample, guide advance, and dispose path allocates exactly zero bytes on the measured thread.
- `NavigationQueryLimits` gains exactly three ceilings: covered ray addresses, ray trace intervals, and expanded A* guide points.
- Superseded APIs are deleted. Do not add obsolete members, aliases, compatibility constructors, optional compatibility parameters, or forwarding overloads.
- Keep `docs/feature-work/gridTopologyNavigationMapRefactorPlan.md` current after every accepted slice, including Phase 7 ownership and deletion residue.
- All `dotnet` commands run serially with `-m:1`; Trailblazer local-stack commands include `-p:UseLocalLsfStack=true`.

---

## File Structure

### FixedMathSharp

- `src/FixedMathSharp/Geometry/Primitives/Segments/FixedSegment2d.cs` — public exact finite-segment minimum-distance decision on the existing owner.
- `src/FixedMathSharp/Geometry/Wide/Common/WidePlanarProjection.cs` — wide rational implementation beside the existing projection/distance authority.
- `tests/FixedMathSharp.Tests/Geometry/Primitives/FixedSegment2d.Separation.Tests.cs` — equality, one-raw, degeneracy, crossing, extreme-domain, and allocation tests.

### GridForge

- `src/GridForge/Grids/Topology/GridCellGeometry.NavigationBodySegment.cs` — shared in-prism body-segment clearance and exact two-prism selected-portal traversal authority.
- `src/GridForge/Grids/Topology/GridCellGeometry.NavigationBodyAnchor.cs` — delegates point validation to the segment authority; retains portal-certificate helpers only once.
- `src/GridForge/Grids/Topology/GridNavigationCorridorValidationCursor.cs` — delegates authored corridor legs to the same segment authority.
- `src/GridForge/Utility/GridTracer.TraceIntervals.cs` — finite candidate-grid ceiling and reporting.
- `src/GridForge/Grids/Support/GridTraceInterval.cs` — explicit grid-candidate status/count.
- `src/GridForge/Grids/Managers/GridWorld.cs` and current `CollectGridCandidates` callers — one bounded collection signature; no unbounded forwarding shape.
- `tests/GridForge.Tests/Grids/GridCellGeometryTests.cs` — swept-body and portal matrix.
- `tests/GridForge.Tests/Grids/GridNavigationCorridorValidationCursorTests.cs` — cursor delegation/resume/budget equivalence.
- `tests/GridForge.Tests/Utility/GridTraceIntervalTests.cs` — grid-candidate one-below and exact report counts.
- `tests/GridForge.Benchmarks/Memory/GridTracerBenchmarks.cs` — migrated finite trace call.

### Trailblazer

- `src/Trailblazer/Pathing/Query/NavigationQueryLimits.cs` — three explicit workspace ceilings.
- `src/Trailblazer/Pathing/Search/Ray/NavigationRayStatus.cs` — internal finite status/result/request/endpoint-allowance contracts.
- `src/Trailblazer/Pathing/Search/Ray/NavigationRayWorkspace.cs` — caller-owned fixed arrays, GridForge scratch, chain state, and temporary dependencies.
- `src/Trailblazer/Pathing/Search/Ray/NavigationRayWork.cs` — one resumable graph-connected ray evaluator.
- `src/Trailblazer/Pathing/Search/Ray/NavigationImmediateRayWorkspace.cs` — one context-owned blocking synchronous runner shared by steering and Flow.
- `src/Trailblazer/Pathing/Graph/NavigationSurfaceEdgeEnumerator.cs` — exposes the canonical emitted edge ordinal.
- `src/Trailblazer/Pathing/Graph/NavigationSurfaceEdgeRouteWork.cs` — validates one authored edge's complete active-profile route and emits no synthetic witness anchors.
- `src/Trailblazer/Pathing/Search/AStar/NavigationAStarNodeTable.cs` — stores winning predecessor ordinal.
- `src/Trailblazer/Pathing/Search/AStar/NavigationAStarGuidePoint.cs` — immutable address/position payload item.
- `src/Trailblazer/Pathing/Search/AStar/NavigationAStarWorkspace.cs` — raw/simplified guide buffers, costs, and exclusive ray workspace.
- `src/Trailblazer/Pathing/Search/AStar/NavigationSurfaceAStarWork.cs` — portal expansion, mandatory raw stamp, bounded simplification, final payload.
- `src/Trailblazer/Pathing/Search/AStar/NavigationAStarPayload.cs` — guide-point retention and exact bytes.
- `src/Trailblazer/Pathing/Search/AStar/NavigationAStarGuideLease.cs` — returns retained guide points without graph geometry reconstruction.
- `src/Trailblazer/Pathing/Search/AStar/NavigationEndpointResolutionWork.cs` and `NavigationQueryAdmissionWork.cs` — role-aware nearest-endpoint ray proof.
- `src/Trailblazer/Pathing/TrailblazerPathingService.cs` — owns the immediate workspace and deletes legacy `NeedsPath`.
- `src/Trailblazer/Pathing/Search/Guide/TrailblazerGuideService.cs` — internal direct-ray orchestration.
- `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldGuideLease.cs` and `NavigationSelectedEdgeProgressWork.cs` — fixed cursor-derived same-lease rejoin.
- `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldPayloadCache.cs` — binds pooled guides to the shared immediate workspace, not per-guide ray buffers.
- `src/Trailblazer/Navigation/Steering/NavSteering.LineOfSight.cs`, `NavSteering.Simulation.cs`, `NavSteering.Serialization.cs`, and `NavSteering.cs` — graph direct cadence, Flow rejoin, recovery-A* deletion, and surface LOS API deletion.
- `src/Trailblazer/Pathing/PathManager.cs` — deletes both public surface `NeedsPath` overloads.
- `tests/Trailblazer.Tests/Pathing/Graph/NavigationRayTests.cs` — ordered graph ray matrix.
- `tests/Trailblazer.Tests/Pathing/Graph/NavigationRayConcurrencyTests.cs` — publication race, blocking workspace, and zero allocation.
- Existing endpoint/A*/Flow/service/Navigator/settings/API/architecture tests — integration and deletion evidence.
- `tests/Trailblazer.Tests/Pathing/Graph/Phase6NavigationRayDeterminismMatrixTests.cs` — Debug/Release/Lean digest matrix.
- `tests/Trailblazer.Benchmarks/Pathing/NavigationRayBenchmarks.cs` and benchmark catalog/README — ray, simplification, Flow rejoin, and contention evidence.

---

### Task 1: Exact FixedMath Segment Separation

**Files:**
- Modify: `F:/gamedevrepos/FixedMathSharp/src/FixedMathSharp/Geometry/Primitives/Segments/FixedSegment2d.cs`
- Modify: `F:/gamedevrepos/FixedMathSharp/src/FixedMathSharp/Geometry/Wide/Common/WidePlanarProjection.cs`
- Create: `F:/gamedevrepos/FixedMathSharp/tests/FixedMathSharp.Tests/Geometry/Primitives/FixedSegment2d.Separation.Tests.cs`

**Interfaces:**
- Produces: `public readonly bool FixedSegment2d.IsDistanceAtLeast(FixedSegment2d other, Fixed64 minimumDistance)`.
- Consumes: existing `WidePlanarProjection.RationalDistance`, `GetSegmentDistance`, `CompareDistances`, and `CompareDistanceToRaw`; no rounded closest point becomes decision authority.

- [x] **Step 1: Write the exact-decision RED**

```csharp
[Theory]
[InlineData(0, true)]
[InlineData(1, false)]
public void IsDistanceAtLeast_AcceptsEqualityAndRejectsOneRawPenetration(
    long offsetFromEquality,
    bool expected)
{
    FixedSegment2d path = new(new Vector2d(-Fixed64.One, Fixed64.One),
        new Vector2d(Fixed64.One, Fixed64.One));
    FixedSegment2d wall = new(new Vector2d(-Fixed64.One, Fixed64.Zero),
        new Vector2d(Fixed64.One, Fixed64.Zero));
    Fixed64 radius = Fixed64.FromRaw(Fixed64.One.m_rawValue + offsetFromEquality);

    Assert.Equal(expected, path.IsDistanceAtLeast(wall, radius));
}
```

Add crossing, parallel-disjoint, point/segment, both-points, oblique hex-like edge, maximum raw coordinate, negative-distance argument, reverse-order, and 256-iteration warmed 0 B cases.

- [x] **Step 2: Run the focused RED**

Run from `F:/gamedevrepos/FixedMathSharp`:

```powershell
dotnet test tests/FixedMathSharp.Tests/FixedMathSharp.Tests.csproj --configuration Release -m:1 --filter FullyQualifiedName~FixedSegment2dSeparationTests
```

Expected: compile failure `CS1061` for missing `IsDistanceAtLeast`.

- [x] **Step 3: Implement the exact wide predicate**

```csharp
public readonly bool IsDistanceAtLeast(
    FixedSegment2d other,
    Fixed64 minimumDistance)
{
    if (minimumDistance < Fixed64.Zero)
        throw new ArgumentOutOfRangeException(nameof(minimumDistance));
    return WidePlanarProjection.IsSegmentDistanceAtLeast(
        this,
        other,
        minimumDistance);
}
```

For a positive minimum, first use `FixedSegment2d`'s existing exact unique-intersection authority. The existing wide projection owner then compares all four exact endpoint-to-opposite-segment rational distances directly to `minimumDistance`; collinear overlap is rejected by its zero endpoint distance. Do not materialize a closest point or rounded distance, and do not add one-method partial files.

- [x] **Step 4: Run focused and full FixedMathSharp gates**

```powershell
dotnet test tests/FixedMathSharp.Tests/FixedMathSharp.Tests.csproj --configuration Release -m:1 --filter FullyQualifiedName~FixedSegment2dSeparationTests
dotnet test FixedMathSharp.slnx --configuration Release -m:1
dotnet test FixedMathSharp.slnx --configuration ReleaseLean -m:1
dotnet build src/FixedMathSharp/FixedMathSharp.csproj --configuration Release -m:1 -f netstandard2.1
dotnet build src/FixedMathSharp/FixedMathSharp.csproj --configuration Release -m:1 -f net8.0
```

Expected: all tests pass; both target frameworks build with zero warnings/errors.

- [x] **Step 5: Request correctness/ponytail review, apply only evidence-backed fixes, and commit**

Closure: FixedMathSharp `fdc1484`; focused `8/8`; Release `2,684 + 8`;
ReleaseLean `2,663 + 8`; netstandard2.1/net8.0 zero warnings/errors;
correctness and ponytail approved; warmed 256-call batch allocated 0 B.

```powershell
git add -- src/FixedMathSharp/Geometry/Primitives/Segments/FixedSegment2d.cs src/FixedMathSharp/Geometry/Wide/Common/WidePlanarProjection.cs tests/FixedMathSharp.Tests/Geometry/Primitives/FixedSegment2d.Separation.Tests.cs
git diff --cached --check
git commit -m "feat(geometry): compare exact segment separation"
```

---

### Task 1b: FixedMath Contact Membership And Parameter Enclosures

**Completed:** `80e019a` adds the endpoint-authored capsule parameter enclosure;
`e400999` adds exact `FixedSegment.Contains(Vector3d)` and the one-solve nearest
plus lower/upper unique-intersection enclosure. These are distinct conservative
contracts, not compatibility overloads. Release 2,687/2,687, ReleaseLean
2,666/2,666, both target frameworks warning-free, warmed allocation 0 B, and
independent correctness/ponytail approval.

---

### Task 2: Shared GridForge Navigation-Body Segment Authority

**Completed:** GridForge `1ed5479` after independent correctness and ponytail
approval. Focused swept-body/cursor coverage passed 60/60; full Release and
ReleaseLean passed 710/710; both target frameworks and the benchmark project
built warning-free; warmed validation allocated 0 B and retained the 224-byte,
2N+1-work-unit cursor contract.

**Files:**
- Create: `F:/gamedevrepos/GridForge/src/GridForge/Grids/Topology/GridCellGeometry.NavigationBodySegment.cs`
- Modify: `F:/gamedevrepos/GridForge/src/GridForge/Grids/Topology/GridCellGeometry.NavigationBodyAnchor.cs`
- Modify: `F:/gamedevrepos/GridForge/src/GridForge/Grids/Topology/GridNavigationCorridorValidationCursor.cs`
- Modify: `F:/gamedevrepos/GridForge/tests/GridForge.Tests/Grids/GridCellGeometryTests.cs`
- Modify: `F:/gamedevrepos/GridForge/tests/GridForge.Tests/Grids/GridNavigationCorridorValidationCursorTests.cs`

**Interfaces:**
- Consumes: `FixedSegment2d.IsDistanceAtLeast`,
  `TryGetCapsuleIntersectionParameterEnclosure`, exact
  `FixedSegment.Contains`, and
  `TryGetUniqueIntersectionParameterEnclosure` from Tasks 1/1b plus existing
  exact `GridNavigationPortal` certificate/profile fields.
- Produces:

```csharp
public static bool IsNavigationBodySegmentValid(
    in GridCellPrism prism,
    Vector3d footStart,
    Vector3d footEnd,
    Fixed64 horizontalRadius,
    Fixed64 bodyHeight,
    in GridNavigationPortal incomingPortal,
    in GridNavigationPortal outgoingPortal);

public static bool TryGetNavigationPortalTraversalParameters(
    in GridCellPrism sourcePrism,
    in GridCellPrism targetPrism,
    in GridNavigationPortal portal,
    Vector3d footStart,
    Vector3d footEnd,
    Fixed64 horizontalRadius,
    Fixed64 bodyHeight,
    out Fixed64 sourceParameter,
    out Fixed64 targetParameter);
```

- [x] **Step 1: Write the swept-body RED matrix**

Add rectangular, pointy-hex, and flat-hex tests for: straight clearance; midpoint corner clip with both endpoint anchors valid; equality/one-raw wall penetration; selected vertical portal approach/cross/reverse; partial opening/endcap clipping; changing Y across the portal; horizontal portal source/target parameters and two-prism body union in both directions; invalid/foreign/ambiguous certificate; extreme domain; and warm 0 B. Add a cursor test proving a multi-cell corridor rejects the same mid-leg clip that the direct segment API rejects.

- [x] **Step 2: Run the focused RED**

```powershell
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj --configuration Release -m:1 --filter "FullyQualifiedName~GridCellGeometryTests|FullyQualifiedName~GridNavigationCorridorValidationCursorTests"
```

Expected: `CS0117` for the two missing APIs, after test fixtures compile.

- [x] **Step 3: Implement one shared segment core**

Implement validation in this order:

1. validate radius/height and prism/certificate arguments;
2. for an in-prism leg, require both segment endpoints and body extents inside that exact prism except at a certified incoming/outgoing portal endpoint;
3. use `FixedSegment2d.IsDistanceAtLeast` against complete non-selected wall segments;
4. for a selected vertical portal, require one exact certified prism edge and validate the segment against every blocked complement of the complete retained contact opening; keep each portal's profile height band active only over its full conservative capsule-overlap enclosure;
5. use `TryGetNavigationPortalTraversalParameters` for an authored source/target prism pair: a vertical portal returns a directed source/target enclosure whose reconstructed endpoints are contained by the corresponding prisms; a horizontal portal returns ordered source/target parameters while the portal's exact resolved anchors remain authoritative. Validate the enclosed vertical gap against every wall of both prisms. A degenerate point/anchor may use a selected opening for clearance but must not fabricate a traversal;
6. fail closed on overflow, collinearity ambiguity, foreign certificates, or two certificates claiming the same crossing inconsistently.

The Phase 6 authority deliberately rejects a one-segment same-wall switch
between two portals' vertical bands. Preserve an intermediate anchor. Phase 7
owns exact inner/outer interval authority plus a fixed three-slice proof if a
real volume/hybrid consumer requires the direct handoff.

Change `IsNavigationBodyAnchorValid` to call the new segment method with `footStart == footEnd`; delete duplicated point-only wall-clearance decisions after parity tests pass. Make corridor validation call the in-prism segment authority for every emitted leg and the two-prism traversal primitive for every authored portal transition, including the distinct vertical movement between horizontal-portal source/target feet. Do not retain an anchor-only wall-clearance alternative.

- [x] **Step 4: Run focused, allocation, and full GridForge gates**

```powershell
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj --configuration Release -m:1 --filter "FullyQualifiedName~GridCellGeometryTests|FullyQualifiedName~GridNavigationCorridorValidationCursorTests"
dotnet test GridForge.slnx --configuration Release -m:1
dotnet test GridForge.slnx --configuration ReleaseLean -m:1
dotnet build src/GridForge/GridForge.csproj --configuration Release -m:1 -f netstandard2.1
dotnet build src/GridForge/GridForge.csproj --configuration Release -m:1 -f net8.0
```

- [x] **Step 5: External re-review and commit**

Require review of selected-opening exactness, vertical/horizontal semantics, equality, overflow, allocation, and deletion of duplicate point logic.

```powershell
git add -- src/GridForge/Grids/Topology/GridCellGeometry.NavigationBodySegment.cs src/GridForge/Grids/Topology/GridCellGeometry.NavigationBodyAnchor.cs src/GridForge/Grids/Topology/GridNavigationCorridorValidationCursor.cs tests/GridForge.Tests/Grids/GridCellGeometryTests.cs tests/GridForge.Tests/Grids/GridNavigationCorridorValidationCursorTests.cs
git diff --cached --check
git commit -m "feat(navigation): certify swept body segments"
```

---

### Task 3: Bounded GridForge Ordered Trace Discovery

**Files:**
- Modify: `F:/gamedevrepos/GridForge/src/GridForge/Utility/GridTracer.TraceIntervals.cs`
- Modify: `F:/gamedevrepos/GridForge/src/GridForge/Grids/Support/GridTraceInterval.cs`
- Modify: `F:/gamedevrepos/GridForge/src/GridForge/Grids/Managers/GridWorld.cs`
- Modify: every current `CollectGridCandidates` and `TraceIntervalsInto` caller returned by `rg`.
- Modify: `F:/gamedevrepos/GridForge/tests/GridForge.Tests/Utility/GridTraceIntervalTests.cs`
- Modify: `F:/gamedevrepos/GridForge/tests/GridForge.Benchmarks/Memory/GridTracerBenchmarks.cs`

**Interfaces:**
- Replaces the old trace signature; no forwarding overload remains:

```csharp
public static GridTraceIntervalReport TraceIntervalsInto(
    GridWorld world,
    Vector3d start,
    Vector3d end,
    SwiftList<GridTraceInterval> results,
    GridTraceIntervalScratch scratch,
    int gridCandidateLimit,
    int addressCandidateLimit,
    int outputLimit);
```

- Adds `GridTraceIntervalStatus.GridCandidateLimitExceeded` and `GridTraceIntervalReport.GridCandidateCount`.
- Replaces internal `GridWorld.CollectGridCandidates` with a bounded bool-returning signature; all callers pass an explicit finite ceiling.

- [ ] **Step 1: Write candidate-grid one-below REDs**

Build overlapping rectangular/pointy/flat grids whose bounds all intersect one segment. Assert exact `GridCandidateCount`, canonical interval order, one-below `GridCandidateLimitExceeded`, cleared output, no address work after grid failure, equality limit success, and warm 0 B.

- [ ] **Step 2: Run the RED**

```powershell
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj --configuration Release -m:1 --filter FullyQualifiedName~GridTraceIntervalTests
```

Expected: compile failures for the new required argument/property/status.

- [ ] **Step 3: Implement bounded collection and migrate all callers**

Collection must stop before appending item `limit + 1`. A failure report preserves the exact candidate-grid count observed up to the ceiling, clears interval/address scratch, and distinguishes grid, address, output, and geometry failures. Other GridForge callers pass an explicit current-world or caller-owned ceiling; none call an unbounded forwarding shape.

- [ ] **Step 4: Verify focused/full builds and API docs**

```powershell
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj --configuration Release -m:1 --filter FullyQualifiedName~GridTraceIntervalTests
dotnet test GridForge.slnx --configuration Release -m:1
dotnet test GridForge.slnx --configuration ReleaseLean -m:1
dotnet build tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj --configuration Release -m:1
rg -n "TraceIntervalsInto\(" src tests -g '*.cs'
```

Every call must show three finite ceilings.

- [ ] **Step 5: Review and commit**

```powershell
git add -- src/GridForge/Utility/GridTracer.TraceIntervals.cs src/GridForge/Grids/Support/GridTraceInterval.cs src/GridForge/Grids/Managers/GridWorld.cs tests/GridForge.Tests/Utility/GridTraceIntervalTests.cs tests/GridForge.Benchmarks/Memory/GridTracerBenchmarks.cs
# Add the exact additional caller files reported by the final TraceIntervalsInto/CollectGridCandidates residue scan.
git diff --cached --check
git commit -m "feat(grids): bound ordered trace grid discovery"
```

---

### Task 4: Trailblazer Ray Contracts, Settings, And Workspaces

**Files:**
- Modify: `src/Trailblazer/Pathing/Query/NavigationQueryLimits.cs`
- Modify: `src/Trailblazer/Pathing/Query/NavigationWorkMeter.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarWorkspace.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldWorkspace.cs`
- Modify: `src/Trailblazer/Pathing/TrailblazerPathingService.cs`
- Create: `src/Trailblazer/Pathing/Search/Ray/NavigationRayStatus.cs`
- Create: `src/Trailblazer/Pathing/Search/Ray/NavigationRayWorkspace.cs`
- Create: `src/Trailblazer/Pathing/Search/Ray/NavigationImmediateRayWorkspace.cs`
- Modify: explicit `new NavigationQueryLimits(...)` callers and public API snapshots.
- Test: `tests/Trailblazer.Tests/Runtime/TrailblazerWorldContextSettingsTests.cs`
- Test: `tests/Trailblazer.Tests/Pathing/Graph/NavigationRayTests.cs`

**Interfaces:**
- Adds constructor parameters/properties `RayWorkspaceCoveredAddressCapacity`, `RayWorkspaceTraceIntervalCapacity`, and `AStarWorkspaceGuidePointCapacity`—no old constructor.
- Produces internal `NavigationRayStatus`, `NavigationRayEndpointAllowance`, `NavigationRayChainConstraint`, `NavigationRayRequest`, and `NavigationRayResult`.
- Extends `NavigationWorkMeter` with exact trace-interval, covered-voxel-interval, and simplification-ray counters backed by the three already-public `NavigationWorkBudget` limits; no second meter owns those query counters.
- `NavigationImmediateRayWorkspace` owns one `object SyncRoot` and one fixed `NavigationRayWorkspace`; it never grows or fails due to thread contention.

- [ ] **Step 1: Write settings/API/workspace REDs**

Pin default values, validation of zero/negative/inconsistent capacities, exact constructor migration, candidate arrays deriving only from the three ceilings plus existing map/page/component capacities, exact one-below work-meter behavior for all three existing budget categories, and one context-owned immediate workspace shared by the guide/pathing services.

- [ ] **Step 2: Run the contract RED**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~TrailblazerWorldContextSettingsTests|FullyQualifiedName~PublicApiSnapshot"
```

Expected: missing constructor arguments/properties and intentional API fingerprint failure.

- [ ] **Step 3: Implement the minimal contracts and fixed storage**

Use this request/result shape:

```csharp
internal readonly struct NavigationRayResult
{
    internal NavigationRayStatus Status { get; }
    internal NavigationCellAddress StartAddress { get; }
    internal NavigationCellAddress EndAddress { get; }
    internal Fixed64 TraversalCost { get; }
    internal bool IsSemanticCostNeutral { get; }
}
```

The workspace preallocates GridForge scratch/results, interval chain records, predecessor/edge ordinals, fixed page/component dependency arrays/sets, and generation stamps. No public ray API is introduced.

- [ ] **Step 4: Focused GREEN and source architecture scan**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~TrailblazerWorldContextSettingsTests|FullyQualifiedName~PublicApiSnapshot"
rg -n "new NavigationQueryLimits\(" src tests -g '*.cs'
```

Every constructor must use the single new shape.

- [ ] **Step 5: Update living tracker and commit**

Mark the ordered-ray slice `In progress`, record the three ceilings, and commit only this contract slice.

```powershell
git add -- src/Trailblazer/Pathing/Query/NavigationQueryLimits.cs src/Trailblazer/Pathing/Query/NavigationWorkMeter.cs src/Trailblazer/Pathing/Search/AStar/NavigationAStarWorkspace.cs src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldWorkspace.cs src/Trailblazer/Pathing/TrailblazerPathingService.cs src/Trailblazer/Pathing/Search/Ray/NavigationRayStatus.cs src/Trailblazer/Pathing/Search/Ray/NavigationRayWorkspace.cs src/Trailblazer/Pathing/Search/Ray/NavigationImmediateRayWorkspace.cs tests/Trailblazer.Tests/Runtime/TrailblazerWorldContextSettingsTests.cs tests/Trailblazer.Tests/Phase0/PublicApiSnapshot.Tests.cs tests/Trailblazer.Tests/Phase0/PublicApiSnapshot.txt docs/feature-work/gridTopologyNavigationMapRefactorPlan.md
# Add the exact explicit NavigationQueryLimits constructor caller files reported by the residue scan.
git diff --cached --check
git commit -m "feat(pathing): define bounded navigation ray workspaces"
```

---

### Task 5: Ordered Graph Navigation-Ray Core

**Files:**
- Create: `src/Trailblazer/Pathing/Search/Ray/NavigationRayWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/Ray/NavigationRayWorkspace.cs`
- Modify: `src/Trailblazer/Pathing/Graph/NavigationSurfaceEdgeEnumerator.cs`
- Modify: `src/Trailblazer/Pathing/Graph/TraversalEvaluator.cs` only if a cost-neutral fact is not already directly observable.
- Test: `tests/Trailblazer.Tests/Pathing/Graph/NavigationRayTests.cs`
- Test: `tests/Trailblazer.Tests/Pathing/Graph/NavigationRayConcurrencyTests.cs`

**Interfaces:**
- `NavigationSurfaceEdgeEnumerator.CurrentOrdinal` is the zero-based canonical ordinal of the currently emitted edge.
- `NavigationRayWork.Begin(in NavigationRayRequest request)` resets without allocation.
- `NavigationRayRequest` explicitly carries `GridWorld`, the graph store, the leased expected graph, resolved `NavigationAgentProfile`, resolved `NavigationAreaPolicy`, `TraversalIntent`, transition permission, directed endpoints, endpoint allowances, and an optional `NavigationRayChainConstraint`. It does not retain a complete `PathQuery` or algorithm/Flow options that the kernel never reads.
- `NavigationRayChainConstraint` has exactly three modes: unrestricted, current-source-address only, or current source followed by one exact canonical selected edge `(source, target, ordinal)` with no substitute graph edge.
- Query and guide `Advance` entry points run one common iterative state machine. Each state exposes the next concrete debit; thin overloads consume it from `NavigationWorkMeter` or `ref GuideSampleWorkMeter` without an interface, boxing, or duplicated transition logic.

- [ ] **Step 1: Write the core RED matrix**

Tests must cover dense/sparse rectangular, pointy/flat hex, mixed overlapping mapped/unmapped grids, disconnected overlap, interior sparse hole, native/seam/explicit multi-witness edges, off-line explicit portal, reverse/asymmetric portal, transitive tie frontier requiring opposite address order, wrong selected overlap, positive radius corner clip, horizontal/vertical portal primitive consumption, every one-below counter, every terminal status, mutation before/after final validation, and exact cost/cost-neutral facts.

- [ ] **Step 2: Run the core RED**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~NavigationRayTests|FullyQualifiedName~NavigationRayConcurrencyTests"
```

Expected: missing `NavigationRayWork` behavior and failing requested statuses.

- [ ] **Step 3: Implement the iterative state machine**

The state machine must:

1. call bounded `GridTracer.TraceIntervalsInto` and debit grid/address/output counts exactly once;
2. map physical intervals to exact immutable graph nodes and record pages/components;
3. partition transitive tie frontiers and repeatedly relax canonical graph edges until closure;
4. require target intervals, exact vertical crossing parameters or ordered horizontal source/target parameters, interval overlap at each parameter, and the exact optional chain constraint;
5. run `TraversalEvaluator` and explicit legs through the same query/guide meter mapping;
6. validate each selected prism segment through `GridCellGeometry.IsNavigationBodySegmentValid`;
7. accumulate exact edge cost and semantic-surcharge neutrality;
8. revalidate dependencies plus `store.Current` before exposing `Success`.

For query work, trace intervals debit `MaxTraceIntervals`, covered addresses debit `MaxCoveredVoxelIntervals`, and each optional simplification attempt debits `MaxSimplificationRays`; existing lookup/edge/connection categories continue to meter their existing semantic work. The guide adapter maps the same state transitions onto current-node, portal, prism, trace, and local-recovery categories without inventing an unbounded allowance.

No recursion, hash iteration, geometry reconstruction, or input-order result dependency is permitted.

- [ ] **Step 4: Run focused, relevant aggregate, allocation, and both TFM builds**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~NavigationRay|FullyQualifiedName~TraversalEvaluator|FullyQualifiedName~NavigationExplicitConnection|FullyQualifiedName~NavigationAutomaticSeam"
dotnet build src/Trailblazer/Trailblazer.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true -f netstandard2.1
dotnet build src/Trailblazer/Trailblazer.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true -f net8.0
```

- [ ] **Step 5: External correctness/ponytail review and commit**

Review graph-chain closure, meter accounting, staleness linearization, lock order, zero allocations, and absence of duplicate geometry.

```powershell
git add -- src/Trailblazer/Pathing/Search/Ray src/Trailblazer/Pathing/Graph/NavigationSurfaceEdgeEnumerator.cs src/Trailblazer/Pathing/Graph/TraversalEvaluator.cs tests/Trailblazer.Tests/Pathing/Graph/NavigationRayTests.cs tests/Trailblazer.Tests/Pathing/Graph/NavigationRayConcurrencyTests.cs docs/feature-work/gridTopologyNavigationMapRefactorPlan.md
git diff --cached --check
git commit -m "feat(pathing): evaluate ordered navigation rays"
```

---

### Task 6: Role-Aware Endpoint Ray Proof

**Files:**
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationEndpointResolutionWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationQueryAdmissionWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/NavigationEndpointWorkspace.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarWorkspace.cs` to pass its exclusive ray workspace into endpoint admission.
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldWorkspace.cs` to pass its exclusive ray workspace into endpoint admission.
- Test: `tests/Trailblazer.Tests/Pathing/Graph/NavigationEndpointResolutionTests.cs`
- Test: A*/Flow admission tests.

**Interfaces:**
- Add internal `NavigationEndpointRole.Start` and `.Destination`.
- `NearestNavigable` candidate proof runs requested start → candidate anchor with only a start prefix allowance, or candidate anchor → requested destination with only a destination suffix allowance.
- Strict resolution remains exact and never consumes the allowance.

- [ ] **Step 1: Write endpoint REDs**

Pin: allowed sparse start prefix; allowed sparse destination suffix; forbidden interior gap; asymmetric portal forward/reverse; nearest geometrically close but graph-unreachable candidate loses to farther reachable candidate; budget/capacity/stale propagation; Flow and A* share the exact role behavior.

- [ ] **Step 2: Observe RED**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~NavigationEndpointResolutionTests|FullyQualifiedName~NavigationAStarAdmissionTests|FullyQualifiedName~NavigationFlowAdmissionTests"
```

- [ ] **Step 3: Integrate resumable candidate rays**

Hold one pending candidate while its ray returns `Pending`; rank only `Success`; skip `Blocked`; propagate budget/cost/capacity/stale exactly. Never expose an endpoint-allowance success as an ordinary full ray result.

- [ ] **Step 4: Focused GREEN, tracker update, and commit**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~NavigationEndpointResolutionTests|FullyQualifiedName~NavigationAStarAdmissionTests|FullyQualifiedName~NavigationFlowAdmissionTests"
git add -- src/Trailblazer/Pathing/Search/AStar/NavigationEndpointResolutionWork.cs src/Trailblazer/Pathing/Search/AStar/NavigationQueryAdmissionWork.cs src/Trailblazer/Pathing/Search/NavigationEndpointWorkspace.cs src/Trailblazer/Pathing/Search/AStar/NavigationAStarWorkspace.cs src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldWorkspace.cs tests/Trailblazer.Tests/Pathing/Graph/NavigationEndpointResolutionTests.cs tests/Trailblazer.Tests/Pathing/Graph/NavigationAStarAdmissionTests.cs tests/Trailblazer.Tests/Pathing/Graph/NavigationFlowAdmissionTests.cs docs/feature-work/gridTopologyNavigationMapRefactorPlan.md
git diff --cached --check
git commit -m "feat(pathing): certify nearest graph endpoints"
```

---

### Task 7: Portal-Correct A* Guide Payloads

**Files:**
- Create: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarGuidePoint.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarNodeTable.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarWorkspace.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationSurfaceAStarWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarPayload.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarPayloadCache.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarGuideLease.cs`
- Create: `src/Trailblazer/Pathing/Graph/NavigationSurfaceEdgeRouteWork.cs`
- Modify: A* admission maximum-byte reservation.
- Test: `tests/Trailblazer.Tests/Pathing/Graph/NavigationSurfaceAStarTests.cs`
- Test: `tests/Trailblazer.Tests/Pathing/Graph/NavigationAStarConcurrencyTests.cs`
- Test: `tests/Trailblazer.Tests/Pathing/Graph/NavigationPublicGuideMatrixTests.cs`

**Interfaces:**

```csharp
internal readonly struct NavigationAStarGuidePoint
{
    internal NavigationCellAddress Address { get; }
    internal Vector3d Position { get; }
}
```

`NavigationAStarPayload.GuidePoints` replaces `Nodes`; no compatibility property remains. Public `NavigationGuideLease.TryGetCurrentWaypoint` is unchanged.

- [ ] **Step 1: Write raw-guide REDs**

Assert native/seam active-profile portal anchors, explicit zero/multi-witness active-profile portal anchors without witness-foot waypoints, horizontal source/target anchors, source-to-entry and exit-to-target mid-leg corner clips selecting an alternate valid edge or producing NoPath, duplicate removal, canonical predecessor ordinal under equal-cost competition, guide-point capacity one-below, exact retained bytes, duplicate cache convergence, mutation staleness, and unchanged public lease shape.

- [ ] **Step 2: Observe RED**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~NavigationSurfaceAStarTests|FullyQualifiedName~NavigationPublicGuideMatrixTests"
```

- [ ] **Step 3: Implement canonical predecessor and raw expansion**

Store `ParentEdgeOrdinal` on every winning relaxation. Before relaxing an edge, run `NavigationSurfaceEdgeRouteWork` over its complete active-profile authored route and skip the edge when any source-foot-to-entry/portal, portal-to-portal, or exit-to-target-foot swept leg fails. During reconstruction, re-enumerate the parent in canonical order to that ordinal, verify the exact child, and rerun the same route work against the current snapshot before emitting source foot, active-profile portal source/target points in semantic order, explicit entry and exit anchors, and target foot. Explicit witness cell foot anchors are never emitted; the compiled portals already define their corridor. Associate every point with a stable address and keep parallel workspace metadata that marks exact node-foot anchors. Assign cumulative raw cost only at the reached target node-foot anchor, where the complete edge cost becomes exact; intermediate portal/connection points inherit the preceding node cost. Remove exact consecutive duplicate positions.

The search and reconstruction calls share the same iterative helper and GridForge authorities. Geometry-invalid edges are rejected during relaxation so A* can choose an alternate route; a publication/dependency change before reconstruction returns `Stale` rather than publishing an uncertified raw leg.

- [ ] **Step 4: Convert payload/cache/guide accounting**

Use the exact runtime size of `NavigationAStarGuidePoint`; reserve maximum bytes from `AStarWorkspaceGuidePointCapacity`; make NoPath carry an empty array; return stored positions directly from the guide lease; update all clone/equality/accounting tests and snapshots. Do not keep `Nodes` as an alias.

- [ ] **Step 5: Run focused/full A* gates, review, tracker update, commit**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~NavigationSurfaceAStar|FullyQualifiedName~NavigationAStar|FullyQualifiedName~NavigationPublicGuideMatrix"
git add -- src/Trailblazer/Pathing/Search/AStar src/Trailblazer/Pathing/Graph/NavigationSurfaceEdgeRouteWork.cs tests/Trailblazer.Tests/Pathing/Graph docs/feature-work/gridTopologyNavigationMapRefactorPlan.md
git diff --cached --check
git commit -m "feat(pathing): retain portal-correct A-star guides"
```

---

### Task 8: Bounded Canonical A* Simplification

**Files:**
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationSurfaceAStarWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/NavigationAStarWorkspace.cs`
- Modify: `src/Trailblazer/Pathing/Search/Ray/NavigationRayWorkspace.cs`
- Test: `tests/Trailblazer.Tests/Pathing/Graph/NavigationSurfaceAStarTests.cs`
- Test: `tests/Trailblazer.Tests/Pathing/Graph/NavigationAStarConcurrencyTests.cs`

**Interfaces:**
- Simplification is payload-construction-only and uses the A* workspace's exclusive `NavigationRayWork`.
- The payload still reports the original optimal graph `TotalCost`.

- [ ] **Step 1: Write simplification REDs**

Pin farthest-valid node-anchor selection, blocked-farthest/nearer fallback, portal points never becoming shortcut endpoints, zero-ray raw byte identity, one-ray deterministic partial result, ray budget exhaustion retaining a valid raw suffix, weighted shortcut cost greater/equal/less than the exact node-anchor raw subroute, candidate dependency merge, union-capacity fallback, mutation invalidation, Debug/Release byte identity, and zero warmed allocation.

- [ ] **Step 2: Observe RED**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~NavigationSurfaceAStarTests|FullyQualifiedName~NavigationAStarConcurrencyTests"
```

- [ ] **Step 3: Implement mandatory-raw-first optional work**

Keep the complete raw dependency set in fixed workspace storage and reserve the exact sort/copy work needed to publish that raw set before optional rays begin. From each exact node-foot anchor, attempt later node-foot anchors in farthest-to-nearest route order; portal and connection points remain raw-guide-only. Commit only a `Success` whose `TraversalCost` is no greater than the exact difference between the two node-anchor cumulative costs. Hold ray dependencies in temporary sorted scratch; before committing, require enough fixed capacity and unreserved lookup work to merge them into the final set. If optional ray count/work/union capacity is unavailable, stop and append the untouched raw suffix. Sort/capture the final raw-plus-accepted-ray dependency stamp exactly once. `Stale` remains terminal; optional blocked/cost-ineligible candidates continue.

- [ ] **Step 4: Run focused/allocation/determinism gates and review**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~NavigationSurfaceAStar|FullyQualifiedName~NavigationAStarConcurrency"
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration ReleaseLean -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~NavigationSurfaceAStar|FullyQualifiedName~NavigationAStarConcurrency"
```

- [ ] **Step 5: Tracker update and commit**

```powershell
git add -- src/Trailblazer/Pathing/Search/AStar src/Trailblazer/Pathing/Search/Ray tests/Trailblazer.Tests/Pathing/Graph docs/feature-work/gridTopologyNavigationMapRefactorPlan.md
git diff --cached --check
git commit -m "feat(pathing): simplify certified A-star guides"
```

---

### Task 9: Graph Direct Travel And Surface LOS Deletion

**Files:**
- Modify: `src/Trailblazer/Pathing/Search/Guide/TrailblazerGuideService.cs`
- Modify: `src/Trailblazer/Pathing/TrailblazerPathingService.cs`
- Modify: `src/Trailblazer/Pathing/PathManager.cs`
- Modify/Delete: surface members in `src/Trailblazer/Navigation/Steering/NavSteering.LineOfSight.cs`
- Modify: `src/Trailblazer/Navigation/Steering/NavSteering.Simulation.cs`
- Modify: tests under PathManager, GuideService, Navigator, NavSteering, architecture, and API snapshot.

**Interfaces:**
- Adds only internal `TrailblazerGuideService.TryGetDirectHeading(PathQuery query, Vector3d actualFoot, out Vector3d heading)` returning `NavigationRayStatus`.
- Deletes both public `PathManager.NeedsPath` overloads, internal `TrailblazerPathingService.NeedsPath`, and public surface `NavSteering.IsDestinationInSight`; no aliases remain.
- Retains explicitly volume-only `IsVolumeDestinationInSight` for the Phase 7 ledger.

- [ ] **Step 1: Write direct-travel and deletion REDs**

Pin initial direct A*/Flow travel without guide acquisition, periodic guide release after a later clear ray, expensive passable terrain retaining weighted guidance, blocked/budget/cost/capacity fallback, stale retry, no `Pending` escape, arrival before combined steering, group steering not replacing retry-zero, blocking two-thread workspace result identity, warm 0 B, and compile/API source invariants proving deleted methods are absent.

- [ ] **Step 2: Observe RED**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~TrailblazerGuideServiceTests|FullyQualifiedName~NavigatorTests|FullyQualifiedName~PathingNavigationMap|FullyQualifiedName~NavigationSearchArchitectureTests|FullyQualifiedName~PublicApiSnapshot"
```

- [ ] **Step 3: Implement direct orchestration and delete legacy surface authority**

Under the immediate workspace lock, acquire one graph lease, resolve only the query semantics consumed by `NavigationRayRequest`, create a fresh `NavigationWorkMeter(query.Budget)`, and run the ray synchronously to a terminal status. Translate a geometrically successful but semantic-cost-ineligible result to `Blocked` at this orchestration boundary, so `Success` always exposes an eligible heading. Wire pre-guide and cooldown checks through this method. Delete old surface LOS production/tests/docs rather than forwarding them.

- [ ] **Step 4: Run focused controller/API/allocation gates and residue scans**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~TrailblazerGuideServiceTests|FullyQualifiedName~NavigatorTests|FullyQualifiedName~NavSteering|FullyQualifiedName~PublicApiSnapshot|FullyQualifiedName~NavigationSearchArchitectureTests"
rg -n "\bNeedsPath\b|IsDestinationInSight" src tests -g '*.cs'
```

Expected residue: only `IsVolumeDestinationInSight`; zero surface/forwarding authority.

- [ ] **Step 5: Review, tracker update, and commit**

```powershell
git add --all -- src/Trailblazer/Pathing/Search/Guide/TrailblazerGuideService.cs src/Trailblazer/Pathing/TrailblazerPathingService.cs src/Trailblazer/Pathing/PathManager.cs src/Trailblazer/Navigation/Steering/NavSteering.LineOfSight.cs src/Trailblazer/Navigation/Steering/NavSteering.Simulation.cs tests/Trailblazer.Tests docs/feature-work/gridTopologyNavigationMapRefactorPlan.md
git diff --cached --check
git commit -m "feat(navigation): steer through certified graph rays"
```

---

### Task 10: Same-Lease Flow Local Rejoin And Recovery-A* Deletion

**Files:**
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationSelectedEdgeProgressWork.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldGuideLease.cs`
- Modify: `src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldPayloadCache.cs`
- Modify: `src/Trailblazer/Pathing/Search/Guide/TrailblazerGuideService.cs`
- Modify: `src/Trailblazer/Navigation/Steering/NavSteering.cs`
- Modify: `src/Trailblazer/Navigation/Steering/NavSteering.Simulation.cs`
- Modify: `src/Trailblazer/Navigation/Steering/NavSteering.Serialization.cs`
- Modify: Flow guide/concurrency/Navigator/architecture tests.

**Interfaces:**
- Pooled Flow guides receive one reference to the context-owned `NavigationImmediateRayWorkspace`; no guide owns ray scratch.
- `NavigationSelectedEdgeProgressWork` exposes one fixed `NavigationFlowRejoinTarget` by stable ordinal, reusing its current selected-edge/corridor enumeration; no candidate array or variable-sized target value is materialized.
- Deletes `_flowRecoveryGuideLease`, `TryGetFlowRecoveryHeading`, its sole `ponytail:` comment, and every recovery A* lifecycle branch.

- [ ] **Step 1: Write Flow rejoin and deletion REDs**

Pin exact rebase first; displaced source-anchor ray constrained to the current source address; selected native/seam/explicit portal/target ray constrained to current source plus the exact canonical selected edge; rejection of an unrelated neutral corridor; cost-ineligible candidate; blocked candidates to `LocalRecoveryRequired`; exactly one total local-recovery debit; meter exhaustion to `BudgetExceeded`; cost/capacity propagation; publication race to sticky `Stale`; only success heading; no cursor mutation until exact node rebase; same Flow lease/cache identity; zero A* admissions; copied lease ABA/double dispose; two-guide blocking workspace determinism; warm 0 B; and zero recovery symbol/comment residue.

- [ ] **Step 2: Observe RED**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~NavigationFlowFieldGuideTests|FullyQualifiedName~NavigationFlowFieldSamplingConcurrencyTests|FullyQualifiedName~NavigatorTests|FullyQualifiedName~NavigationSearchArchitectureTests"
```

- [ ] **Step 3: Implement fixed-candidate ray rejoin**

Keep the existing exact covered-address rebase first. Move the existing `TryRequireLocalRecovery` debit into the rejoin branch so the entire failed-rebase/rejoin attempt consumes exactly one local-recovery unit total. Enumerate current source then selected-edge portal/target anchors one at a time by stable ordinal and ray-test each immediately. The source target uses the source-only chain constraint; every selected-edge target requires current source followed by the exact canonical selected edge. Do not scan payload nodes, materialize candidates, rebuild Flow, or submit A*. Commit only `Success`; preserve status mapping and sticky staleness exactly.

- [ ] **Step 4: Delete recovery bridge and verify lifecycle**

Remove fields/helpers/serialization cleanup/tests for the temporary A* bridge. Preserve the authoritative Flow query and one Flow lease across retry-neutral frames. `LocalRecoveryRequired`, `BudgetExceeded`, and other retry-neutral zero-heading frames retain that same Flow lease/query and bypass combined steering; they do not trigger a hidden guide rebuild or terminal arrival. Ensure arrival/stop/replacement/load releases exactly once.

- [ ] **Step 5: Run focused Flow/controller/allocation gates, review, tracker update, commit**

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true --filter "FullyQualifiedName~NavigationFlowField|FullyQualifiedName~NavigatorTests|FullyQualifiedName~NavigationSearchArchitectureTests"
rg -n "_flowRecoveryGuideLease|TryGetFlowRecoveryHeading|ponytail:" src tests docs/feature-work/gridTopologyNavigationMapRefactorPlan.md -g '*.cs' -g '*.md'
git add --all -- src/Trailblazer/Pathing/Search/Flow/NavigationSelectedEdgeProgressWork.cs src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldGuideLease.cs src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldPayloadCache.cs src/Trailblazer/Pathing/Search/Guide/TrailblazerGuideService.cs src/Trailblazer/Navigation/Steering/NavSteering.cs src/Trailblazer/Navigation/Steering/NavSteering.Simulation.cs src/Trailblazer/Navigation/Steering/NavSteering.Serialization.cs tests/Trailblazer.Tests docs/feature-work/gridTopologyNavigationMapRefactorPlan.md
git diff --cached --check
git commit -m "feat(pathing): rejoin flow fields through navigation rays"
```

Expected: no source recovery symbol/comment; the tracker records deletion and Phase 7 ownership only.

---

### Task 11: Phase 6 Determinism, Benchmarks, Documentation, And Exit Gates

**Files:**
- Create: `tests/Trailblazer.Tests/Pathing/Graph/Phase6NavigationRayDeterminismMatrixTests.cs`
- Create: `tests/Trailblazer.Benchmarks/Pathing/NavigationRayBenchmarks.cs`
- Modify: benchmark catalog/program/README.
- Modify: `README.md`, `docs/wiki/Overview.md`, `docs/wiki/Navigator.md`, `docs/wiki/NavSteering.md`.
- Modify: `docs/feature-work/gridTopologyNavigationMapRefactorPlan.md`.
- Modify: API/architecture/deletion snapshots only for intentional final surface.

**Interfaces:**
- Benchmark alias: `navigation-ray` with short/medium/long, sparse blocked, mixed seam/explicit, worst guide points, bounded simplification, Flow rejoin, and immediate-workspace contention cases.
- Determinism output prefix: `PHASE6_RAY_DIGEST` with canonical sorted fields and checked-in hashes.

- [ ] **Step 1: Write determinism and benchmark catalog REDs**

The digest matrix must cover rectangular, pointy, flat, tie/overlap, endpoint, simplified A*, direct steering, Flow rejoin, and mutation/serialization-visible controller state. The benchmark catalog test/list command must fail until `navigation-ray` resolves.

- [ ] **Step 2: Implement the smallest real benchmark/digest harness**

Use production worlds/graphs, exact counters, preflight semantic assertions, warmed allocation checks, and bounded BDN jobs. Do not publish timing claims until all semantic preflights pass.

- [ ] **Step 3: Run 13 serial determinism processes**

Run Debug, Release, ten additional Release processes, and direct ReleaseLean. Extract/sort exactly the expected digest lines and compare byte-for-byte plus SHA-256. Store ignored evidence under `.superpowers/sdd/2026-08-16-phase6-navigation-rays/`.

- [ ] **Step 4: Run bounded smoke then canonical benchmarks**

```powershell
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj --configuration Release -- navigation-ray --job short --inProcess
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj --configuration Release -- navigation-ray
```

Require every case discovered/executed, zero BDN issue/error patterns, semantic counters/drains green, and p50/p95/p99/max/allocation archived without unsupported claims.

- [ ] **Step 5: Update docs and living Phase 7 handoff**

Document one graph ray/direct/simplified-guide/Flow-rejoin behavior. The tracker must explicitly retain Phase 7 ownership of volume ray wiring, `VolumeVoxelFinder.IsDirectPathClear`, `IsVolumeDestinationInSight`, full media/transition semantics, and the pre-release public/internal API decision. Do not describe a test-only runtime activation.

- [ ] **Step 6: Run final cross-repository gates serially**

FixedMathSharp:

```powershell
dotnet test FixedMathSharp.slnx --configuration Release -m:1
dotnet test FixedMathSharp.slnx --configuration ReleaseLean -m:1
```

GridForge:

```powershell
dotnet test GridForge.slnx --configuration Release -m:1
dotnet test GridForge.slnx --configuration ReleaseLean -m:1
```

Trailblazer:

```powershell
dotnet restore Trailblazer.slnx -p:UseLocalLsfStack=true
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration ReleaseLean -m:1 -p:UseLocalLsfStack=true
dotnet build src/Trailblazer/Trailblazer.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true -f netstandard2.1
dotnet build src/Trailblazer/Trailblazer.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true -f net8.0
dotnet build src/Trailblazer/Trailblazer.csproj --configuration ReleaseLean -m:1 -p:UseLocalLsfStack=true -f netstandard2.1
dotnet build src/Trailblazer/Trailblazer.csproj --configuration ReleaseLean -m:1 -p:UseLocalLsfStack=true -f net8.0
dotnet pack src/Trailblazer/Trailblazer.csproj --configuration Release -m:1 -p:UseLocalLsfStack=true
```

- [ ] **Step 7: Final scans and independent reviews**

Require zero forbidden surface LOS/recovery/forwarding symbols, zero Trailblazer topology projection/collision duplicates, zero new LINQ/float/recursive hot code, correct API snapshot, clean package contents, clean `git diff --check`, and correctness plus ponytail approvals over the entire Phase 6 range.

- [ ] **Step 8: Commit final evidence/docs slices and mark Phase 6 complete**

Use focused commits for determinism, benchmarks, docs/ledger, and any review-only cuts. The final tracker row records commit hashes, test counts, benchmark artifact paths, determinism hash, allocation evidence, residual Phase 7 debt, and no unowned legacy surface API.
