# Trailblazer Contributor Guide

## Purpose

Trailblazer is a deterministic, engine-agnostic navigation and character
controller library for lockstep simulations and games. Correctness and
cross-runtime determinism come before convenience or raw speed.

The library targets `netstandard2.1` and `net8.0`, uses C# 11, and relies on
FixedMathSharp for simulation math.

## Start Here

Read these in order before making a non-trivial change:

1. [`README.md`](README.md) for package orientation and documentation routes.
2. [`docs/wiki/GettingStarted.md`](docs/wiki/GettingStarted.md) for the public
   integration path and [`docs/wiki/Overview.md`](docs/wiki/Overview.md) for
   architecture and ownership.
3. [`Trailblazer.slnx`](Trailblazer.slnx),
   [`src/Trailblazer/Trailblazer.csproj`](src/Trailblazer/Trailblazer.csproj),
   and the project file for the matching test or benchmark area.
4. the relevant folder under [`src/Trailblazer`](src/Trailblazer)
5. the matching area under [`tests/Trailblazer.Tests`](tests/Trailblazer.Tests)
6. [`tests/Trailblazer.Benchmarks/README.md`](tests/Trailblazer.Benchmarks/README.md)
   before changing measured hot paths or benchmark cases.
7. [`docs/feature-work/feature-work-overview.md`](docs/feature-work/feature-work-overview.md)
   and the evergreen issue or benchmark tracker when a task belongs to an active
   coordination thread.

Read [`docs/wiki/Serialization.md`](docs/wiki/Serialization.md) before changing
records or load behavior. Read
[`docs/wiki/MapPublication.md`](docs/wiki/MapPublication.md) before changing
map, overlay, policy, or graph lifecycle behavior.

## Source Of Truth

Code and tests are authoritative when prose disagrees. Keep public behavior
aligned across:

- [`README.md`](README.md);
- [`AGENTS.md`](AGENTS.md) and [`CONTRIBUTING.md`](CONTRIBUTING.md);
- [`docs/wiki/Overview.md`](docs/wiki/Overview.md);
- the relevant wiki reference;
- [`docs/api`](docs/api) for the generated API-site configuration, landing
  content, namespace overrides, logo, repository link, and custom theme;
- [`docs/feature-work`](docs/feature-work) for tracker state and active plans;
- public XML comments;
- source, tests, benchmarks, and relevant workflows.

Keep links inside `docs/wiki` repository-friendly with their `.md` extensions.
The wiki workflow rewrites only the published copy. Treat `docs/api/index.md`,
`docs/api/toc.yml`, DocFX configuration, templates, and overwrite files as
authored source. Never edit or commit generated `docs/api/obj` output.

## Repository Map

| Path                                                               | Purpose                                                                               |
| ------------------------------------------------------------------ | ------------------------------------------------------------------------------------- |
| [`src/Trailblazer/Runtime`](src/Trailblazer/Runtime)               | World context, clock, settings, and lifecycle ownership                               |
| [`src/Trailblazer/Pathing/Map`](src/Trailblazer/Pathing/Map)       | Immutable maps, cells, connections, transitions, overlays, and publication operations |
| [`src/Trailblazer/Pathing/Graph`](src/Trailblazer/Pathing/Graph)   | Immutable graph composition, dependencies, topology traversal, and diagnostics        |
| [`src/Trailblazer/Pathing/Query`](src/Trailblazer/Pathing/Query)   | Public query, endpoint, profile, budget, status, and medium contracts                 |
| [`src/Trailblazer/Pathing/Search`](src/Trailblazer/Pathing/Search) | Endpoint admission, A\*, Flow, guide leases, and internal navigation rays             |
| [`src/Trailblazer/Navigation`](src/Trailblazer/Navigation)         | Navigator, steering, turning, motor, locomotion, occupancy, and controller records    |
| [`src/Trailblazer/Heightmaps`](src/Trailblazer/Heightmaps)         | Context-owned deterministic ground-height storage and sampling                        |
| [`tests/Trailblazer.Tests`](tests/Trailblazer.Tests)               | xUnit v3 behavior, determinism, allocation, serialization, and API coverage           |
| [`tests/Trailblazer.Benchmarks`](tests/Trailblazer.Benchmarks)     | BenchmarkDotNet performance and semantic preflight scenarios                          |
| [`docs/api`](docs/api)                                             | DocFX configuration, branded landing content, namespace overrides, and theme          |
| [`docs/wiki`](docs/wiki)                                           | Behavioral and integration guides synced to the GitHub Wiki                           |
| [`docs/feature-work`](docs/feature-work)                           | Active plans plus evergreen issue and benchmark-signal trackers                       |

## Runtime Architecture

The optional `src/Trailblazer.Gravitas` project packages `GravitasNavigator3D`.
Core must not acquire a Gravitas dependency. Follow
[Gravitas.md](docs/wiki/Gravitas.md) for the shared-world all-prepare /
one-resolve / all-commit barrier.

One `TrailblazerWorldContext` owns one active GridForge `GridWorld` binding and
all Trailblazer state for that world.

The normal host lifecycle is:

1. create an owned context or attach one to a host-owned `GridWorld`;
2. create GridForge grids;
3. build immutable `NavigationMap` values and exact `NavigationAreaPolicy`
   revisions;
4. admit maps, policies, removals, and overlays through `context.Pathing`;
5. call `context.Simulate()` once per deterministic fixed frame to advance graph
   publication, scheduled acquisition within shared frame allowances, and
   ordered simulation hooks;
6. acquire A\* or Flow leases through `context.Guides`, synchronously or by
   polling an owned scheduled request after each fixed frame;
7. drive `Navigator.Simulate()` and `Navigator.CommitFrameMotion()` for
   controller-owned movement;
8. dispose leases, Navigators, and then the context.

`PathQuery` is the only public A\*/Flow request. Search state is an exact
`NavigationCellAddress` plus `TraversalMedium`. Solid, Gas, and Liquid movement
share one graph, cost model, dependency system, and guide contract.

`TrailblazerGuideService` returns immutable-payload leases. Cursor, current
medium, and pending transition state belong to each lease acquisition. Never
cross an action by advancing an ordinary step; execute the host action once and
complete its exact `NavigationTransitionInstruction`.

Scheduled acquisition is one bounded FIFO across both algorithms. Preserve
frozen contiguous cohorts, oldest-first builds, independently admitted follower
endpoint proofs, and capacity-accounted source-pin/lease ownership. Covered
followers may finish before uncovered members within that frozen cohort.
Preserve deterministic submission order, query total budgets, exactly-once
result transfer and cancellation before pathing reset/disposal. Completed
unclaimed results consume retention capacity. Counted slices do not bound atomic
ray, allocation or publication time; measure full containing frames before
making timing claims. The canonical contract is in
[`Pathing.md`](docs/wiki/Pathing.md#spreading-acquisition-across-fixed-frames).

Navigator defaults to Immediate acquisition; Scheduled is explicit durable
policy. Waiting yields zero guided heading but must preserve
motor/gravity/platform and commit processing. Moved-origin A\* adoption must
prove the exact ordinary index-zero anchor, reprove from the current foot on
every connector heading under the existing fair validation turn, and
retain/account proof only on that lease. Mode changes control future
acquisition, not permission to bypass an adopted connector. Release its
proof/interest on advancement and every session/reset/teardown boundary.

## Determinism Rules

The context composes Chronicler's clock: absolute frames are `long`, elapsed
time and jump-start times are `ChronicleTimestamp`, and integration steps stay
Fixed64. Subtract timestamps before bounded conversion; do not recreate a
narrow or unsigned parallel timeline. `GetFrameCountForDuration` counts complete
current steps, not historical frames. Preserve clock-lifetime checks at reset
and reject stale prepared motion before changing accepted pose or occupancy.

Always prefer:

- `Fixed64`, `Vector3d`, and `FixedQuaternion` over floating-point simulation
  math;
- explicit frame state from `TrailblazerWorldContext`;
- stable canonical ordering at authoring/publication boundaries;
- explicit capacities, budgets, statuses, and serialization schemas;
- iterative bounded work in hot paths.

Do not introduce:

- `float`, `double`, or `System.Numerics` into deterministic runtime logic;
- wall-clock time, timers, unseeded randomness, or platform-dependent ordering;
- hidden unbounded scans, recursion, or storage growth;
- engine APIs in the core package;
- search-time terrain/material callbacks whose result can change without
  publication.

FixedMathSharp owns deterministic math. GridForge owns topology, world identity,
cell prisms, contacts, and covered-body geometry. Trailblazer owns navigation
semantics, search, dependencies, guide orchestration, and controllers. Hosts own
terrain classification, physics, animation, and semantic action execution.

## Public API And Documentation

Public APIs should be explicit, difficult to misuse, and documented with concise
XML summaries. Breaking changes are acceptable only when they intentionally
improve the long-term contract.

When changing public behavior:

1. add or update behavior tests;
2. update README/wiki guidance, public XML, and the API landing/namespace
   content in the same change when affected;
3. update JSON and MemoryPack coverage when wire behavior changes.

Examples presented as runnable C# must use current public signatures. Label
partial snippets, host placeholders, or engine-adapter pseudocode explicitly.

## Serialization

Trailblazer uses explicit Chronicler `IRecordable.RecordData(...)` paths. JSON
and MemoryPack are active in the standard package; `ReleaseLean` omits the
MemoryPack transport.

The load model is populate-existing-instance only:

- hosts create and initialize runtime shells first;
- grids, maps, policies, and persisted overlays are restored before guided
  Navigators;
- staged validation completes before live shell mutation;
- host bindings, graph payloads, guide leases/cursors, pending actions,
  scheduled requests/continuations, dependencies, and committed-cell
  notifications are not serialized;
- old schema shapes reject instead of flowing through compatibility aliases.

## Performance And Collections

Correctness and determinism precede performance. For hot paths:

- avoid allocations, LINQ, and hidden collection growth;
- prefer SwiftCollections when a suitable deterministic low-allocation type
  exists;
- preserve obvious ownership for pooled or retained state;
- measure before adding caches, topology-specific paths, or copies;
- update exact retained-byte and allocation gates when layouts change.

Cache reuse must never weaken dependency validation. A stale guide is worse than
a small allocation.

## Testing Workflow

`global.json` selects the .NET 10 SDK for `.slnx` tooling consistency. Install
the .NET 8 runtime as well to execute the `net8.0` tests and benchmarks. CI
validates Windows and Linux in both `Release` and `ReleaseLean`.

```bash
dotnet restore Trailblazer.slnx --property:Configuration=Release
dotnet build Trailblazer.slnx --configuration Release --no-restore
dotnet test Trailblazer.slnx --configuration Release --no-build
```

Repeat with `ReleaseLean`. The library itself must build for both
`netstandard2.1` and `net8.0`; tests and benchmarks target `net8.0`.

Build the generated API site after a Release build:

```bash
dotnet tool restore
dotnet tool run docfx docs/api/docfx.json --warningsAsErrors
```

CI enforces exact reachable coverage in both package configurations. After a
successful push to `main`, the coverage workflow checks out the exact tested
commit, builds and validates the DocFX site, and deploys one Pages artifact with
the API site at the root and coverage under `/coverage/`. The separate wiki
workflow publishes `docs/wiki` from that tested commit.

For focused work, filter the test project first:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~NavSteering
```

The library generates packages during build. Keep standard and Lean dependency
families consistent; do not mix package families in one validation run.

For coordinated source development, use `UseLocalLsfStack=true` as a command-line
global property or the environment of a freshly launched IDE. A repo-only local
property may not reach sibling NuGet restores. See
[CONTRIBUTING.md](CONTRIBUTING.md#coordinated-source-development-and-ide-restore).
Keep package mode the default; never infer release readiness from source builds.

## Test Design

Use separate core and adapter coverage settings: the adapter's include filter is
`[Trailblazer.Gravitas]*`. Reports must contain both assemblies, not merely show
100% for whichever assembly was instrumented. During coordinated feature work
use `UseLocalLsfStack=true` for source tests; unpublished private-feed consumers
use the normal package graph, an isolated cache, and source mapping. Do not
treat source-mode nuspecs or fixture versions as release artifacts.

Use the existing context-first fixtures:

- [`tests/Trailblazer.Tests/Support/TrailblazerFixture.cs`](tests/Trailblazer.Tests/Support/TrailblazerFixture.cs)
- [`tests/Trailblazer.Tests/Support/PathingFixture.cs`](tests/Trailblazer.Tests/Support/PathingFixture.cs)
- [`tests/Trailblazer.Tests/Support/PathTestFactory.cs`](tests/Trailblazer.Tests/Support/PathTestFactory.cs)
- [`tests/Trailblazer.Tests/Support/GuidedPathTestScene.cs`](tests/Trailblazer.Tests/Support/GuidedPathTestScene.cs)

Tests must dispose successful guide leases and context/world ownership. Prefer
public observable behavior for acceptance tests; internal diagnostics are for
focused accounting or teardown evidence, not substitutes for behavior.

Prioritize:

- deterministic order and exact fixed-point cost;
- sparse, dense, rectangular, pointy-hex, and flat-hex topology;
- capacity/budget one-below boundaries;
- dependency invalidation and publication races;
- transition completion, mismatch, stale, and retry behavior;
- JSON/MemoryPack transactional population;
- zero-allocation steady-state hot paths where required.

## Contributor Workflow

1. Read the canonical docs, source, and matching tests.
2. Identify deterministic, lifetime, and global-world implications.
3. Capture a focused regression before fixing behavior.
4. Make the smallest coherent change and preserve unrelated worktree edits.
5. Run focused `Release` and `ReleaseLean` checks.
6. Run the full required matrix before claiming completion.
7. Update docs/XML when public behavior changed.
8. Report exact failures and evidence; do not hide or work around a red gate.

Keep the root README concise, product-focused, and safe for NuGet rendering. Put
architecture, lifecycle, subsystem behavior, and troubleshooting detail in the
matching wiki page or generated API reference.

Do not stage, commit, tag, push, publish packages, or create releases unless the
user explicitly requests it. Preserve unrelated worktree changes.

Treat graph publication, cache invalidation, transition ownership, controller
load staging, and serialized schemas as high-risk boundaries.

## Shared Diagnostics Ownership

Use the independent `Chronicler.Diagnostics` package for diagnostic channels,
lazy logging handlers, and `ChronicleGuard` guards. It does not depend on
`Chronicler.Core`. Keep library-specific channels, formatters, and logger facades
in their owning library. `UseLocalLsfStack=true` selects the sibling diagnostics
project; standard and Lean builds use the same diagnostics package.
