# Multi-World Phase 0 Baseline

Phase 0 records the current guardrails before moving world-owned runtime state behind
`TrailblazerWorldContext`.

## Compatibility Reference Baseline

- Production files containing `TrailblazerWorldManager`: 28.
- Guard test:
  `MultiWorldArchitectureGuardTests.ProductionTrailblazerWorldManagerReferences_ShouldNotIncreaseBeyondPhase0Baseline`
- Goal: this count should only move downward as later phases replace the ambient bridge with
  context-owned state.

## Engine-Agnostic Timing Guard

- Guard test:
  `MultiWorldArchitectureGuardTests.ProductionSimulationCode_ShouldRemainEngineAgnosticAndFrameDriven`
- The guard rejects production references to common engine namespaces and wall-clock timing APIs in
  runtime code. Trailblazer should stay deterministic, frame-driven, and host-engine agnostic.

## Allocation Signals To Preserve

Existing allocation tests form the Phase 0 baseline:

- `PathGuideFactoryCoverageTests.RequestCacheKeys_ShouldNotAllocateSteadyState`
  - 1,024 aggregate cache-key reads across A*, FlowField, and Volume requests must allocate less
    than 128 bytes.
- `PathGuideFactoryCoverageTests.WarmGuideHits_ShouldAllocateNearZero_WhenReturnedGuidesCanBeReused`
  - 256 warm request/return cycles for A*, FlowField, and Volume guides must allocate less than
    1,024 bytes each.
- `PathGuideFactoryCoverageTests.ReusableSurveyResultCacheWarmCheckout_ShouldNotAllocateSteadyState`
  - 256 warm checkout/return cycles must allocate less than 1,024 bytes.
- `NavSteeringTests.ComputeCombinedSteering_ShouldAvoidSteadyStateAllocation`
  - steady-state combined steering should allocate less than 512 bytes.
- `NavSteeringTests.ScanRadiusInto_ShouldAvoidRepeatedAllocation_ForSteeringOccupants`
  - repeated steering occupant scans should allocate less than 512 bytes.
- `NavSteeringTests.ComputeCombinedSteering_ShouldAvoidSteadyStateAllocation_WhenNearbyOccupantsDoNotSteer`
  - steady-state non-steering occupant scans should allocate less than 512 bytes.

Focused verification:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~PathGuideFactoryCoverageTests
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~NavSteeringTests
```

## Red Acceptance Verification

Focused command:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~MultiWorldPhase0AcceptanceTests
```

Before quarantine, all six acceptance tests failed for the expected current-state reasons:

- duplicate chart names collide through process-wide `PathManager` state
- equivalent request coordinates still lack an explicit context-local guide-cache service
- resetting a previously active `GridWorld` does not clear only that world's live chart state
- `TrailblazerWorldContext` does not yet exist to own independent frame counters
- movement-group state is still process-wide
- navigator reset still lacks an owning world context

Five acceptance tests remain marked with `Category=MultiWorldPhase0Red` and skipped until their
owning implementation phase unskips and satisfies them. The independent
`TrailblazerWorldContext.FrameCount` acceptance test was unskipped after Phase 1 and now passes
against the context shell.
