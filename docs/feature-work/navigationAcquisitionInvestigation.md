# Guide Acquisition Cost Investigation

**Date:** 2026-09-08  
**Scope:** Diagnose `TRB-Benchmark-001`, and determine whether `TRB-Issue-114`
confounds its evidence. No production-runtime fix or scheduler is implemented.

## Findings

Cold and warm acquisition have different dominant costs:

1. Cold Flow and A* spend most sampled request time evaluating surface routes,
   including exact geometric contact, portal and body-segment calculations.
   Two expensive body-segment computations only feed debug assertions, but
   remain active in Release. This is confirmed unused diagnostic work.
2. Large warm Flow acquisition spends most sampled time checking dependency
   stamps. The successful public cache-hit path makes six full stamp passes.
   Those passes protect different publication/ownership boundaries; deleting
   checks indiscriminately would be unsafe.
3. The historical 616-byte covered-test observation is still unattributed.
   It does not invalidate the separate, uninstrumented acquisition evidence.
   No new warmup increase or allocator fix is justified by that observation.

The next implementation should first remove the confirmed Release diagnostic
work, then measure again. Portal reuse and dependency-proof reuse are separately
reviewable optimizations with stronger correctness obligations. The profiles do
not justify a scheduler as the first response or promise that these changes
alone will meet the provisional 2 ms allowance.

The confirmed unused work is tracked as `TRB-Issue-115`; the broader performance
signal remains open as `TRB-Benchmark-001`.

## Source and diagnostic boundary

The investigation uses clean `develop` commit
`ab46543e04f2a625dea5aa36854fa945dbc21ca9`, which commits the
[Phase 3 measurements](navigationAcquisitionMeasurements.md). Its coordinated
local upstream source commits and hardware/runtime remain as recorded there:
.NET 8.0.29 x64, SDK 10.0.302 and Intel Core i7-9700K on Windows.
No sibling repository source was changed.

A temporary .NET 8 diagnostic executable references the prebuilt Release
benchmark assembly and calls the existing `NavigationColdGuideBenchmarks`
setup, preparation, public operation and validation methods. Private non-inlined
`RunColdFlow`, `RunColdAStar` and `RunWarmFlow` methods identify the request
subtrees in the trace. World setup and the constructor's cold reference checks
are excluded by that subtree selection, not subtracted from whole-process time.
There is no source-level runtime instrumentation or coverage collector in these
processes. The copied, benchmark and source-output `Trailblazer.dll` hashes agree:
`F25DC5C7A7C03CEF1978AF6215704715DCA84244644E07F3218BE80B9D8FBB54`.

Cold probes clear caches before each of three requests. Warm probes seed and
validate one field, then run batches of 1,000 acquire/dispose operations so the
profiler can observe the short operation. Every request still checks success
and exact integration cost; full-payload/drain checks and printing are outside
each timed batch. Untimed reflection in the later probe reads existing payload
dependency lengths; reflection is not part of the measured request subtree.

The warm diagnostic loop is deliberately different from BenchmarkDotNet's
single-invocation preparation cadence. It is for attribution, **not a replacement
latency distribution or an optimization result**. In particular, the 10K warm
probe accelerates during its continuous run; this run alone does not attribute
that warmup effect to a specific runtime mechanism.

Traces use locally installed `dotnet-trace` 10.0.731102 with
`dotnet-sampled-thread-time,dotnet-common`. This is managed sampled-thread-time
evidence, not exact per-method CPU accounting: safepoint bias, inlining and
sampling granularity limit interpretation. Inclusive percentages overlap and
must not be added or treated as predicted speedups. Exclusive attribution can
include inlined helpers. See Microsoft's
[trace-tool reference](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
and [profiling limitations](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-highcpu).

## Cold acquisition: shared surface-route cost

Three traced requests per algorithm retain the original exact success/cost
checks and per-request allocations: 779,656 bytes for Flow and 35,320 for A* at
10K nodes. These are diagnostic runs, not new canonical timing gates.

| Request subtree | Sampled time represented | Surface route `AdvanceSimple` inclusive | Portal compilation inclusive | Body-segment validation inclusive |
| --- | ---: | ---: | ---: | ---: |
| Cold public Flow, 10K | 16.435 s | 92.43% | 29.45% | 29.14% |
| Cold public A*, 10K | 6.688 s | 90.91% | 30.80% | 29.20% |

The portal column is `GridCellGeometry.TryCreateNavigationPortal`; the body
column is `GridCellGeometry.IsNavigationBodySegmentValid`. Inlined work may
appear under callers rather than these named frames. Shared hot descendants
include FixedMathSharp's wide convex containment, world-point transforms and
exact segment distances. This identifies expensive *uses* of geometric proof;
it does not establish a defect in the underlying wide arithmetic.

### Confirmed unused Release computations

[NavigationSurfaceEdgeRouteWork.AdvanceSimple](../../src/Trailblazer/Pathing/Graph/NavigationSurfaceEdgeRouteWork.cs)
computes `validSourceSegment` and `validTargetSegment` unconditionally, then
uses them only in `Debug.Assert(validSourceSegment && validTargetSegment)`.
The assertion is omitted in Release; the two method calls are not. The Release
profile confirms that this path executes.

For admitted valid profiles, these stateless calculations do not change the
current Release passability result, cost, route points or work-meter accounting.
The narrow candidate is to make the entire diagnostic computation debug-only,
preserving both checks in Debug. Do not remove the body checks in
`AdvanceExplicit`: those still reject invalid authored corridor segments.

There is a smaller instance in
[TraversalEvaluator.EvaluateNative](../../src/Trailblazer/Pathing/Graph/TraversalEvaluator.cs):
translation of `template` into `retainedPortal` currently feeds only assertions,
while a separately compiled portal supplies the returned evidence.

Git history locates these forms in `c14c3d6891ea0293a880dd4aaed21af365a6b69a`
(`task: close code coverage`). Earlier rejection/equality branches were changed
to assertions, but their prerequisite calculations remained unconditional.
This explains how the work became unused; it is not a measured before/after
comparison of that historical commit.

### Portal recompilation: candidate, not yet a proven safe substitution

Native maps retain an agent-independent portal template per direction, and
automatic seams also retain compiled portals. The evaluator nevertheless calls
GridForge portal compilation during evaluation. That compilation discovers
contact and clips convex footprints again.

Unlike the unused assertion results, the newly compiled portal currently
supplies actual anchors, costs and route evidence. Replacing it with retained
geometry needs exact equivalence tests first. Existing native-template tests
cover anisotropic rectangles, both hex orientations, multiple positions and a
raw-fractional hex case, but Trailblazer's shared comparison helper omits
`VerticalFaceSegmentStart` and `VerticalFaceSegmentEnd`. Those endpoints matter
to swept-body opening validity.

Before native-template reuse, compare the complete portal representation,
profile admission/anchors and resulting fixed-point costs across supported
directions, fractional metrics, origins and positions. Treat automatic seams as
a separate proof boundary. Keep GridForge as the owner of geometric compilation;
do not copy a simplified geometry implementation into Trailblazer.

## Warm acquisition: repeated full dependency proof

The first 100K warm trace contains 20,000 successful acquire/dispose operations
with zero measured allocation in every 1,000-request batch. Its selected request
subtree represents 9.059 seconds of sampled time:

| Boundary | Inclusive share of request subtree | Full payload-stamp passes from source |
| --- | ---: | ---: |
| `NavigationWorldGraph.IsDependencyCurrent` | 95.35% | Six across the following boundaries |
| Cache checkout | 47.68% | Three |
| Public guide creation | 32.45% | Two |
| Cached-result publication | 15.76% | One |

Checkout checks current graph, admitted graph, then current graph after releasing
the cache lock. Publication validates the cached result again. Guide creation
checks under the cache lock and again after binding. Source locations are
[NavigationFlowFieldPayloadCache](../../src/Trailblazer/Pathing/Search/Flow/NavigationFlowFieldPayloadCache.cs)
(`TryCheckout`, `CompletePostLockValidation`, `TryCreateGuide`) and
[NavigationFlowQueryWork.FinishCached](../../src/Trailblazer/Pathing/Search/Flow/NavigationFlowQueryWork.cs).

Each full stamp validation checks the area policy, any transition dependency,
all component dependencies and all page dependencies. The warm hit does not
rebuild/copy the field or execute Flow search. Origin lookup is logarithmic;
the dependency scan is linear in the retained proof size.

A separate 40,000-request 100K repeat represents 18.799 seconds of selected
profile time, with **96.51%** inclusive dependency-validation attribution and
zero measured allocation in all 40 batches. Untimed inspection confirms one
component and 1,563 dependency pages in the 100K payload, compared with one
component and 157 pages at 10K. Six successful full passes therefore visit
9,378 versus 942 page entries per acquisition, before counting component and
policy checks. These counts explain the size sensitivity without asserting that
each lookup has equal cost.

### Safe direction for a later optimization

Investigate reuse of a successful proof for the **same payload and exact
immutable graph object**, initially within a request. Retain every required
current-graph reread, raw-world change-sequence check and lease-generation/
ownership check. If graph identity differs, validate the new root normally.
Graph version alone is not a substitute for exact object identity. Avoid
unbounded memoization or retaining old graph roots without memory accounting.

The narrowest experiment could reuse checkout's immediately preceding successful
current-graph validation only when `ReferenceEquals(current, expectedGraph)`;
that would eliminate one of six scans, not solve the entire warm path. Carrying
a proof across all boundaries additionally requires lifetime review: the current
warm path releases its admission graph lease before publication. Dependency
arrays are immutable by ownership convention rather than by CLR type, so verify
all production mutation paths before general proof reuse.

The existing tests cover newer cached payloads versus older admitted graphs,
mutation before cached-result publication, mutation before guide creation and
publication while a caller waits for the cache lock. Any optimization must retain
those outcomes. This is a direction for a focused proof/review, not permission
to remove the checks or a completed cache design.
Add same-graph/raw-world-change and changed-root/unrelated-dependency cases;
a changed graph must be revalidated, not automatically rejected.

## Issue 114: separate allocation-guard investigation

The reviewed test measures a synchronous acquire/attach/sample/dispose window
with no assertion framework calls or closures inside that window. It has local
fixture/cache ownership and checks both successful sampling and returned leases.
The five-cell fixture differs from the acquisition-only benchmark workload.

Concrete allocation candidates are an empty guide pool, a cache miss republishing
a `CacheEntry`, and an empty/growing graph-lease pool during sampling. Existing
warmup should establish those capacities. Source inspection does not identify
which, if any, caused the historical 616 bytes. Only Trailblazer runtime code is
coverage-instrumented; the test assembly is excluded. Coverage is a discriminating
experimental axis, **not an attributed cause**.

The acquisition probes run separately without the test runner or coverage
collector and omit sampling. Their existing setup asserts zero warm acquisition
allocation, and all observed timed warm batches also report zero. Consequently
Issue 114 does not block this cost investigation.

After all profiling completed, the unchanged core test project passed three
fresh full covered Release runs (**2,395/2,395 each**) and one fresh full covered
ReleaseLean run (**2,333/2,333**), with zero skips. Each run's TRX records
`WarmAttachSampleDispose_ShouldAllocateZeroBytes` as passed. Local-source solution
builds for both configurations completed with zero warnings/errors. These
repeated passes support keeping the mitigation, not closing an intermittent
allocation issue whose source remains unknown. The adapter tests and exact
merged-coverage gate were not rerun for this documentation-only investigation.

If the exact zero guard fails again, use a temporary diagnostic version of that
same full-suite covered test with preallocated numeric storage. Record allocation
deltas and iteration indices around checkout/optional publication, attachment,
sampling and disposal; print only afterward. Preserve the zero-byte assertion.
Then compare coverage on/off for the identified phase. Allocation sampling can
miss a one-off 616-byte event, so a clean sampled trace would not settle it.

## Reproduction and retained evidence

Diagnostic files are retained locally under `artifacts/benchmark001/`, separate
from the untouched Phase 3 benchmark archive. The probe's source and project,
stack analyzer, raw `.nettrace` files and exported Speedscope JSON are retained.
The local ignored archive is
`artifacts/benchmark001/acquisition-investigation-evidence.zip` (39 files,
including covered-test logs, TRX and coverage attachments), SHA256:
`E3CE463E45F8DEB3DFD873E3301A574DAC11C50B89596CD6D321C88D5AE1E8F0`.
Every archived entry was hash-checked against its source file. This is local
evidence, not a published CI artifact; restore its contents under
`artifacts/benchmark001/` to reproduce the diagnostic commands below.
The analyzer selects only the named non-inlined request subtree, checks event
nesting, and verifies that exclusive durations sum to the selected duration.
It removes Speedscope's synthetic `CPU_TIME`/`UNMANAGED_CODE_TIME` leaf labels for
exclusive method attribution. Positive event intervals are not independent
CPU samples; export-time ordering nudges can create tiny intervals.
Synthetic analyzer checks pass for weighted intervals, synthetic-leaf stripping
and subtree filtering. Independent read-only review checked the cold/warm
attribution, source boundaries and coordination records with no findings;
the first warm trace's weighted totals were also independently recalculated
directly from its raw exported events.

Build the existing benchmark against the recorded local stack first, then the
diagnostic executable. Run captures sequentially without competing builds/tests:

```powershell
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -p:UseLocalLsfStack=true
dotnet tool install dotnet-trace --version 10.0.731102 --tool-path artifacts/benchmark001/tools
dotnet build artifacts/benchmark001/probe/AcquisitionProbe.csproj -c Release
artifacts/benchmark001/tools/dotnet-trace collect --profile dotnet-sampled-thread-time,dotnet-common --format Speedscope --output artifacts/benchmark001/cold-flow-10k.nettrace --show-child-io -- dotnet artifacts/benchmark001/probe/bin/Release/net8.0/AcquisitionProbe.dll flow Plane10000 3
```

Use `astar Plane10000 3` for the A* capture, `warm Plane10000 20` for the
small warm diagnostic, and `warm Plane100000 20` for the first large warm
diagnostic, with distinct output paths. The repeat uses `warm Plane100000 40`.
Each warm iteration contains 1,000 requests. The later probe adds untimed payload
metadata reporting; the original cold and first 100K warm captures preceded
that reporting addition, without changing the request methods.

Analyze a capture with, for example:

```powershell
artifacts/benchmark001/analyze-stacks.ps1 -Path artifacts/benchmark001/cold-flow-10k.speedscope.json -Root Program.RunColdFlow
```

Use `Program.RunColdAStar` or `Program.RunWarmFlow` for the corresponding request
subtree. The canonical Phase 3 measurements remain the performance baseline;
none of these profiled loops are claimed as a new SLA or an optimization win.

The covered-test reproduction uses the unchanged collector settings, separate
fresh test processes and unique result directories; repeat Release three times
and ReleaseLean once after building the matching solution configuration:

```powershell
dotnet build Trailblazer.slnx -c Release -p:UseLocalLsfStack=true
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj -c Release --no-build -p:UseLocalLsfStack=true --settings tests/Trailblazer.Tests/coverlet.runsettings --results-directory artifacts/benchmark001/issue114/Release/run1 --logger trx
```
