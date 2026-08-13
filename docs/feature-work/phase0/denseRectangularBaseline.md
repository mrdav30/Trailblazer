# Dense Rectangular Phase 0 Baseline

Phase 0 freezes behavior that must either remain equivalent in the topology-aware
implementation or be called out as an intentional clean break. The executable
contract is `DenseRectangularBehaviorContractTests`.

| Contract | Frozen evidence |
| --- | --- |
| A* route shape | exact waypoints for a forced dense-rectangular detour |
| deterministic tie break | four fresh surveys produce the same exact route around a symmetric blocker |
| dynamic blockers | an exact GridForge obstacle event blocks a route and removal restores it |
| transitions | swim-entry and swim-exit IDs and staged step ordering remain stable |
| flow reuse | covered origins share the same destination-centric survey result |
| guide invalidation | targeted chart invalidation removes cached state and stales a checked-out guide |
| controller waypoint following | `NavSteering` advances a close active waypoint on the guide path |
| public surface | every exported type and declared public member is covered by `PublicApiSnapshotTests` |

Run the behavior and API baseline in Release:

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter "FullyQualifiedName~DenseRectangularBehaviorContractTests|FullyQualifiedName~PublicApiSnapshotTests"
```

## Existing Release Performance Coverage

The BenchmarkDotNet project already carries the dense-rectangular workloads
required to compare the replacement graph against the current implementation:

| Phase 0 measure | Existing benchmark selection |
| --- | --- |
| raw/cold/warm A* and warmed allocations | `a-star-path-request` |
| raw/cold/warm flow and multi-origin reuse | `flow-field-path-request` |
| targeted invalidation and stale-guide lifecycle | `guide-cache` |
| dynamic obstacle repath waves and build/flood scaling | `pathing-scenario` |
| line of sight and guided waypoint steady state | `nav-steering` |
| transition fallback | `transition-fallback` |
| controller-scale fixed steps | `navigation-scenario` |

Capture the machine-specific JSON reference without changing benchmark code:

```powershell
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- a-star-path-request flow-field-path-request guide-cache pathing-scenario nav-steering transition-fallback navigation-scenario --exporters json
```

Timing values are not hard-coded into unit tests because they are runner
dependent. Benchmark setup preflight validates route shape, every benchmark is
covered by `MemoryDiagnoser`, and Phase 2 comparisons use the same selections
and machine/toolchain as this capture.
