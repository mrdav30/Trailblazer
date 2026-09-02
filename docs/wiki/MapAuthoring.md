# Navigation Map Authoring

New authoring produces one immutable `NavigationMap` per normalized GridForge
grid. Hosts may build maps directly, import tokens, or materialize the output of
their own terrain/fluid systems. Runtime search never calls host classification
delegates.

## Direct Builder

This C# fragment assumes a normalized GridForge binding, area IDs, cell indices,
and transition rule have already been created:

```csharp
NavigationCell openGas = new(
    TraversalMedia.Gas,
    TraversalCapability.None,
    areaId,
    enterCost: Fixed64.Zero,
    radiusClearance: Fixed64.One,
    heightClearance: Fixed64.One);

NavigationMap map = new NavigationMapBuilder("airspace", normalizedGrid)
    .SetDefaultCell(openGas)
    .AddCell(blockedOverrideIndex, specialCell)
    .AddTransitionRule(takeoffRule)
    .Build();
```

The builder copies and canonically sorts its input. Duplicate cell indices,
connection IDs, definition IDs within one owner, or globally duplicate rule IDs
reject during validation/publication.

## Dense And Hex Imports

`NavigationMapBuilder.ImportDenseRectangular(...)` and
`ImportAxialHex(...)` adapt topology-local authored data into the same map
format. `NavigationMapTokenImporter.ImportRectangular(...)` provides a concise
token lane backed by `NavigationTokenLegend`.

The built-in token importer can author cell semantics and paired shoreline
transitions. Custom legends remain explicit data: a token resolves to one
complete `NavigationTokenLegendEntry`, not a search-time callback.

## Host Materialization

If a host already has terrain or matter predicates, evaluate them outside the
pathing hot path and publish the result:

| Host result | Map representation |
| --- | --- |
| Most present cells share one state | `SetDefaultCell(...)` |
| A bounded set differs from the default | explicit `AddCell(...)` entries |
| A runtime address changed | `NavigationCellOverlayOperation.Set(...)` |
| A runtime address is intentionally unavailable | `Suppress(...)` |
| Runtime state should reveal the bake again | `RevertToBake(...)` |

For example, a height/biome classifier may decide where terrain is walkable and
emit Solid cells. A fluid simulation may emit Liquid cells. Trailblazer consumes
the materialized cells; it does not define terrain and does not treat a terrain
category as Volume truth.

Materialize in deterministic address order and assign explicit operation
sequences/effective frames. Avoid wall-clock state, nondeterministic collection
iteration, or callbacks whose results can change without publication.

## Choosing A Default

- Use no default for sparse fail-closed authoring.
- Use a Solid default only when every physically present unlisted cell really
  supports grounded travel.
- Use a Gas default for an open atmosphere.
- Use a Liquid default for a completely flooded or submerged space.
- Use a multi-medium default only when the same physical cells genuinely admit
  each listed state.

Changing a default is a new map bake, not a query option. Explicit cells still
win over the new default.

## Authoring Actions

Use `AddTransition(...)` for an object-anchored action such as one ladder,
door, lift, jump, or teleporter. Add both directed definitions when travel must
work both ways.

Use `AddTransitionRule(...)` when one bounded semantic rule should apply at
many local contacts, such as Liquid-to-Gas takeoff at any eligible water
surface. Rules are limited to `SameCell` and `PositiveFaceContact`; they do
not create retained per-cell transition objects.

Do not infer an action from media contact alone. Matter says where a state can
exist; a definition or rule says which semantic action the environment allows.

## Prepare And Publish

This C# fragment assumes the map, deterministic operation sequence, and
effective frame are available:

```csharp
var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
var operation = new NavigationMapCommitOperation(
    prepared,
    OverlayReplacementPolicy.Clear,
    operationSequence,
    effectiveFrame);

if (!context.Pathing.Admit(operation))
{
    // Admission limits rejected the operation.
}
```

The operation receipt is pending until fixed-step maintenance publishes or
rejects it. Do not assume `Admit(...)` means the map is already visible.

## Related guides

- [Navigation maps](NavigationMaps.md)
- [Map publication](MapPublication.md)
- [Transitions](Transitions.md)
- [Volume traversal](VolumeTraversal.md)
