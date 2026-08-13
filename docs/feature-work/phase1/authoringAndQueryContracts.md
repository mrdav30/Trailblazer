# Phase 1 Authoring And Query Contracts

Phase 1 introduces the public navigation authoring and query model that will
survive the topology refactor. It does not route runtime searches through the
new model yet; graph composition and lifecycle integration begin in Phase 2.

## Authoring model

Each `NavigationMap` owns one stable `MapId` and one
`NormalizedGridConfiguration`. The map stores only authored navigation cells,
so its value identity is independent of whether the matching GridForge grid is
dense or sparse. Cells, source-owned connections, and transitions are copied,
validated, and sorted into a deterministic immutable representation.

Authoring entry points include:

- sparse addressed cells through `NavigationMapBuilder`;
- X/Y/Z rectangular arrays through `ImportDenseRectangular`;
- explicit Q/layer/R hex entries through `ImportAxialHex`;
- rectangular token volumes through `NavigationMapTokenImporter`.

Connections use foot-position anchors and an ordered cell-witness chain.
Trailblazer validates local chains during baking and validates cross-map chains
transactionally as soon as every referenced map is present. The topology-owned
GridForge corridor validator proves positive-area portals, body clearance, and
the checked fixed-point canonical corridor cost. An absent cross-map target is
retained as dormant authoring data rather than partially activated.

## Dynamic overlays and publication

`NavigationOverlayTransaction` groups immutable per-map deltas. Cell,
connection, and transition operations support Set/Upsert, Suppress, and
RevertToBake semantics. Preparation is inert; operation sequence and effective
frame determine deterministic publication order.

Candidate folding is transactional. A validation or capacity failure preserves
the previously published maps and overlays. The operation limits bound pending
descriptors, prepared-map bytes, batch work, corridor scratch, active maps, and
retained map identities, plus per-map/context overlay entries. Receipts publish
one thread-safe terminal status: Applied, Rejected, or Superseded.

Eligible publication batches are hard-capped at 256 operations. This bounds the
conservative receipt-coalescing pass; larger admitted prefixes carry over in
stable sequence order. A separate benchmark case folds 100,000 addressed cell
changes inside one immutable overlay transaction.

Checkpoint rebakes carry the exact bake version and overlay sequence they
absorbed. Bake versions for a `MapId` must increase monotonically, including
after removal, so an old checkpoint cannot alias a different bake.

## Query model

`PathQuery` is immutable intent. It combines explicit start/end endpoint
resolution, a `NavigationAgentProfile`, traversal intent, algorithm selection,
flow-field options, and a finite `NavigationWorkBudget`.

`KinematicBodyShape` is the shared KCC/navigation geometry: non-negative radius,
positive height, and non-negative root-to-foot offset. Endpoints and authored
anchors are foot positions. No query value is inferred from grid cell metrics.

`GuideSampleWorkBudget` separately bounds work performed while consuming a
guide. The complete budget surface includes lookup probes, endpoint candidates,
node/edge work, transition and staged-route work, trace intervals, covered
voxel intervals, simplification rays, cursor scans/rebases, and portal/prism
checks.

## Performance evidence

Phase 1 includes BenchmarkDotNet workloads for canonical map baking,
connection-order normalization, and operation folding. The initial Dry-run
measurements are directional rather than release gates:

| Workload | Observed result |
| --- | ---: |
| 1,000-cell map bake | 8.024 ms, 55.49 KB |
| 100,000-cell map bake | 19.042 ms, 5.34 MB |
| 1,000,000-cell map bake | 101.174 ms, 53.41 MB |
| 1,000-connection canonicalization | 96.26 ms forward / 96.20 ms reverse |
| 100,000-cell overlay prepare/admit/fold | 14.279 ms, 16.00 MB |

The builder reuses one maximum-chain geometry scratch allocation per bake.
The operation processor owns fixed batch/corridor scratch, and a warmed empty
frame allocates zero bytes.

## Local-stack validation

Until the matching GridForge changes are released, pass the local-stack switch
as an MSBuild command-line property; `.slnx` does not store arbitrary build
properties:

```powershell
dotnet build Trailblazer.slnx -c Release -p:UseLocalLsfStack=true
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj -c Release -p:UseLocalLsfStack=true
```

Use the direct test/benchmark project commands for release-quality local-stack
validation because they propagate the selected configuration through sibling
project references consistently. Package-based validation becomes authoritative
again after the upstream releases are published.

`UseLocalLsfStack` is the switch that selects the conditional project-reference
group. The `AdditionalProperties` metadata on those references does not select
local source: it propagates the active `Configuration` and pins each sibling's
exact released `SemVer`. Keep those exact values rather than `SemVer=*`.
The sibling projects otherwise default to assembly version `0.0.0`, while parts
of the current local graph still consume the corresponding 7.0.0 packages; the
pin prevents duplicate same-named assemblies with incompatible identities.

A command-line property applies only to that invocation. An IDE that was opened
normally therefore continues to evaluate the package-reference group. To make
IDE design-time builds and dependency browsing use local source, close the IDE,
set the environment property, and launch the IDE from that shell:

```powershell
$env:UseLocalLsfStack = 'true'
devenv .\Trailblazer.slnx
```

Rider or another IDE may be launched the same way. Remove the environment value
and restart the IDE to return to package references. The `.slnx` file itself does
not persist arbitrary MSBuild properties.
