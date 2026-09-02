# Getting Started

This guide takes a new .NET project from installation to its first deterministic
A* route. The example is deliberately small, but it uses the same world, map,
publication, query, and lease lifecycle as a production integration.

## Choose a package

Most applications should start with the standard package:

```bash
dotnet add package Trailblazer
```

Use the Lean package when the rest of your LSF dependency stack also uses Lean
packages:

```bash
dotnet add package Trailblazer.Lean
```

| Package | Use it when... |
| --- | --- |
| `Trailblazer` | You want JSON and MemoryPack serialization support. |
| `Trailblazer.Lean` | You want the same navigation API without the MemoryPack transport. |

Do not mix standard and Lean LSF packages in one dependency graph.

Trailblazer targets `netstandard2.1` and `net8.0`. The examples on this page use
a .NET 8 console application.

## Know the five pieces

Before the code, it helps to name the ownership boundaries:

1. `GridWorld` owns physical grids, cells, topology, blockage, and geometry.
2. `TrailblazerWorldContext` owns navigation state for exactly one active
   GridForge world.
3. `NavigationMap` gives physically present cells navigation meaning.
4. `NavigationAreaPolicy` says which authored areas a query may enter and what
   extra cost they add.
5. `PathQuery` describes one complete A* or Flow request.

Trailblazer never discovers terrain or performs gameplay actions for you. The
host materializes those decisions as maps, overlays, policies, and explicit
transitions.

## Run your first route

Create a console project, install `Trailblazer`, and replace `Program.cs` with
the complete example below. It creates three adjacent physical cells, gives
them all Solid navigation meaning, publishes one permissive area policy, and
requests an A* route from the first cell to the last.

```csharp
using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer;
using Trailblazer.Pathing;

const string mapId = "getting-started";

var gridConfiguration = new GridConfiguration(
    Vector3d.Zero,
    new Vector3d(2, 0, 0),
    topologyKind: GridTopologyKind.RectangularPrism,
    topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
    storageKind: GridStorageKind.Dense);

var world = new GridWorld();
if (!world.TryAddGrid(gridConfiguration, out _))
{
    world.Dispose();
    throw new InvalidOperationException("The demo grid could not be registered.");
}

using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(
    world,
    takeOwnership: true);

if (!gridConfiguration.TryNormalize(out NormalizedGridConfiguration gridBinding))
    throw new InvalidOperationException("The demo grid configuration is invalid.");

var openCell = new NavigationCell(
    TraversalMedia.Solid,
    TraversalCapability.None,
    area: default,
    enterCost: Fixed64.Zero,
    radiusClearance: Fixed64.One,
    heightClearance: Fixed64.One);

NavigationMap map = new NavigationMapBuilder(mapId, gridBinding)
    .AddCell(new VoxelIndex(0, 0, 0), openCell)
    .AddCell(new VoxelIndex(1, 0, 0), openCell)
    .AddCell(new VoxelIndex(2, 0, 0), openCell)
    .Build();

var mapOperation = new NavigationMapCommitOperation(
    new PreparedNavigationMap(map, bakeVersion: 1),
    OverlayReplacementPolicy.Clear,
    operationSequence: 1,
    effectiveFrame: context.FrameCount + 1);

var policyKey = new NavigationAreaPolicyKey("default", revision: 1);
var policyOperation = new NavigationAreaPolicyCommitOperation(
    new NavigationAreaPolicy(
        policyKey,
        new[]
        {
            new NavigationAreaRule(
                isAllowed: true,
                additionalEnterCost: Fixed64.Zero)
        }),
    publicationSequence: 2,
    effectiveFrame: context.FrameCount + 1);

if (!context.Pathing.Admit(mapOperation)
    || !context.Pathing.Admit(policyOperation))
{
    throw new InvalidOperationException("Navigation publication was not admitted.");
}

for (int frame = 0;
    frame < 4_096
    && (mapOperation.Receipt.Status == NavigationOperationStatus.Pending
        || policyOperation.Receipt.Status == NavigationOperationStatus.Pending);
    frame++)
{
    context.Simulate();
}

if (mapOperation.Receipt.Status != NavigationOperationStatus.Applied
    || policyOperation.Receipt.Status != NavigationOperationStatus.Applied)
{
    throw new InvalidOperationException("Navigation publication did not complete.");
}

var startIndex = new VoxelIndex(0, 0, 0);
var destinationIndex = new VoxelIndex(2, 0, 0);

if (!gridBinding.TryGetCellPrism(startIndex, out GridCellPrism startPrism)
    || !gridBinding.TryGetCellPrism(destinationIndex, out GridCellPrism destinationPrism))
{
    throw new InvalidOperationException("The demo endpoints are outside the grid.");
}

Vector3d startFoot = new(
    startPrism.Center.X,
    startPrism.VerticalMin,
    startPrism.Center.Z);
Vector3d destinationFoot = new(
    destinationPrism.Center.X,
    destinationPrism.VerticalMin,
    destinationPrism.Center.Z);

var profile = new NavigationAgentProfile(
    new KinematicBodyShape(
        radius: Fixed64.Quarter,
        height: Fixed64.One,
        rootToFootOffsetY: Fixed64.Zero),
    maxStepUp: Fixed64.One,
    maxDropDown: Fixed64.One,
    arrivalRadius: Fixed64.Quarter,
    allowedMedia: TraversalMedia.Solid,
    capabilities: TraversalCapability.None);

var query = new PathQuery(
    new NavigationEndpoint(startFoot, mapId),
    new NavigationEndpoint(destinationFoot, mapId),
    profile,
    policyKey,
    new TraversalIntent(
        TraversalMedium.Solid,
        TraversalMedia.Solid),
    PathAlgorithm.AStar,
    new NavigationWorkBudget(
        maxLookupProbes: 4_096,
        maxEndpointCandidates: 4_096,
        maxExpandedNodes: 4_096,
        maxEvaluatedEdges: 4_096,
        maxConnectionLegs: 4_096,
        maxTransitionCandidates: 0,
        maxTransitionPairs: 0,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: 0,
        maxCoveredVoxelIntervals: 0,
        maxSimplificationRays: 0),
    allowTransitions: false);

NavigationGuideStatus status = context.Guides.RequestGuide(
    query,
    out NavigationGuideLease? acquired);

if (status != NavigationGuideStatus.Success || !acquired.HasValue)
    throw new InvalidOperationException($"Route request failed: {status}.");

using NavigationGuideLease guide = acquired.Value;

while (guide.TryGetCurrentStep(out NavigationGuideStep step)
       == NavigationGuideStatus.Success)
{
    Console.WriteLine(
        $"{guide.CurrentStepIndex}: {step.Address} at {step.Position}");

    if (guide.CurrentStepIndex == guide.StepCount - 1)
        break;

    if (guide.TryAdvanceStep() != NavigationGuideStatus.Success)
        throw new InvalidOperationException("The guide could not advance.");
}
```

The final step is the destination. It remains current, so the example stops
instead of trying to advance beyond it. In a game, the host would move toward
each step over fixed frames rather than print them immediately.

## What changes in a real integration

The demo uses the simplest possible choices:

- one owned world context;
- one dense rectangular grid;
- three explicit Solid cells;
- one permissive area rule;
- no transitions;
- one A* consumer.

A production host will usually materialize cells from terrain or fluid data,
publish runtime overlays, use explicit capabilities and area costs, and keep
the context alive across many fixed frames. The lifecycle does not change.

For many agents sharing a destination, set the query algorithm to
`PathAlgorithm.FlowField` and request a Flow lease. For ladders, jumps,
takeoff, or other actions, author transitions and complete their exact
instructions after the host performs the action.

## Next steps

- [Technical Overview](Overview.md) — understand ownership and lifecycle.
- [Map Authoring](MapAuthoring.md) — build useful maps from host data.
- [Map Publication](MapPublication.md) — publish deterministic runtime changes.
- [Pathing](Pathing.md) — choose endpoints, profiles, budgets, and algorithms.
- [Path Guides](PathGuides.md) — consume movement and action steps safely.
- [Troubleshooting](Troubleshooting.md) — diagnose common integration failures.
- [v1 to v2 Migration Guide](../MIGRATION.md) — update an existing project.
