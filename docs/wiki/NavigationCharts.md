# Navigation Maps And Effective Cells

Unified graph routing is authored through `NavigationMap`, not through a
runtime terrain predicate or a traversal partition. One map binds one stable map
ID to one normalized GridForge configuration.

> The current package still exposes an older chart-management surface for
> compatibility with non-graph callers. It is not an input to unified A*/Flow
> routing and is scheduled for removal by the repository tracker. New
> integrations should publish `NavigationMap` values.

## 1. Map Contents

An immutable map contains:

- `MapId`;
- `NormalizedGridConfiguration GridBinding`;
- optional `DefaultCell`;
- canonically ordered explicit `Cells`;
- directed physical `Connections`;
- directed semantic `Transitions`;
- bounded procedural `TransitionRules`.

`NavigationMapBuilder.Build()` validates duplicates, topology-local indices,
cell payloads, transition identities, and rule payloads before creating the
immutable bake. Context-wide and per-map transition-rule limits are enforced at
publication.

## 2. Complete Cell Payloads

`NavigationCell` stores:

- one or more supported `TraversalMedia`;
- required capabilities;
- a stable area ID;
- nonnegative enter cost;
- horizontal radius and vertical clearance;
- optional transition-generation hints.

Cell media describes state of matter at that address. Terrain is not a separate
runtime truth inside Trailblazer. A host can use terrain, scene metadata, fluid
simulation, or any other source to decide which complete cell to publish.

## 3. Default And Explicit Entries

The optional map default applies to every physically present, in-bounds
GridForge cell that has no higher-precedence semantic cell. It does not create a
GridForge cell and does not fill an absent sparse address.

Explicit entries override the default. This makes both dense and sparse
authoring cheap:

- a Gas-default map can list only walls or special areas;
- a Liquid-default map can list islands or hazards;
- a map with no default is fail-closed except at explicit entries.

## 4. Runtime Precedence

The effective cell order is:

1. overlay cell;
2. explicit baked entry;
3. map default;
4. no navigation cell.

Each layer replaces the entire cell. For example, an overlay that changes Gas to
Liquid must also provide the intended area, cost, clearance, capabilities, and
flags.

`NavigationCellOverlayOperation.Suppress(index)` hides every lower layer.
`RevertToBake(index)` removes the overlay decision and reveals the explicit
bake, then the default, then no cell.

## 5. Physical And Semantic Independence

GridForge owns whether an address exists, is blocked, and which exact prism and
contacts it has. Trailblazer owns the semantic payload admitted at that address.

Consequences:

- a default never materializes absent physical storage;
- an authored semantic cell at an absent sparse address is dormant;
- a GridForge add/remove or blockage event can activate or deactivate graph
  state without rewriting the map;
- map and physical dependencies both participate in guide staleness.

## 6. Medium-State Connectivity

Structural connectivity uses positive-face contact:

- rectangular prisms: six faces;
- pointy/flat hex prisms: six planar faces plus top and bottom.

The same physical cell may have Solid, Gas, and Liquid search states. Native
movement retains medium. Volume shortcuts improve route quality but never create
connectivity on their own. Semantic transitions are the only edges that can
change medium.

## 7. Publication

A map is prepared with `PreparedNavigationMap` and admitted with
`NavigationMapCommitOperation`. Replacement requires a higher bake version for
the same map ID. Choose:

- `OverlayReplacementPolicy.PreserveAndRevalidate` to preserve the exact
  overlay/dynamic-address set and validate the complete recomposed candidate;
- `OverlayReplacementPolicy.Clear` to discard them atomically.

Preservation never silently prunes an invalid overlay. If the preserved set is
incompatible with the replacement bake, the whole map replacement rejects
transactionally.

Runtime cell/connection/transition changes use one
`NavigationOverlayCommitOperation`. See [Runtime publication](PathManager.md).

## 8. Related References

- [Map authoring](ChartAuthoring.md)
- [Pathing](Pathing.md)
- [Volume traversal](VolumeTraversal.md)
- [Transitions](Transitions.md)
