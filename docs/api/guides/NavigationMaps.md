# Navigation Maps And Effective Cells

Unified graph routing is authored through `NavigationMap`, not through a runtime
terrain predicate or a separate medium-specific routing system. One map binds
one stable map ID to one normalized GridForge configuration.

## Map Contents

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

## Top-Down Layer Binding

`NavigationMapBuilder.SetPlanarLayer(NavigationMode2D.TopDown, layerIndex, embeddingY)`
selects one topology-local layer for a top-down bake. Use `SideView` for a
host-resolved side-view controller domain. The modes cannot share route coverage
or cross-mode actions. The embedding must be
within that layer's closed world-Y range. Explicit cells must belong to the
selected layer. `NavigationMap.PlanarBinding` preserves the mode, layer, and
embedding in the bake's identity. It is null for spatial maps.

Prepare and publish a top-down bake through the ordinary map operation. Its
default and cell overlays apply only on the selected layer, even when its
embedding touches an adjacent layer. A live map ID keeps its planar binding
through replacement. Remove the map before publishing the ID in a different
domain or with a different layer or embedding.

Hosts can check [circle placement](Pathing.md#strict-top-down-placement)
and [clearance between neighboring cells](Pathing.md#top-down-leg-clearance)
on these maps, or find an [immediate route](Pathing.md#immediate-top-down-routes)
between cells on compatible published maps at the same embedding.

## Side-View Support

Select `NavigationMode2D.SideView` and use `AddPlanarSupport(index, left, right)`
to author a finite support surface for a cell. Supply the endpoints in world
X/Z with increasing X. Each selected-layer cell owns at most one segment. Both
endpoints must lie within its closed footprint. A support edit changes the
bake's identity and uses the ordinary map replacement operation.

The effective cell still controls whether a support can anchor Solid travel.
It must be physically present and unblocked, permit Solid, and admit the
capsule's radius and total height. The profile must hold its required
capabilities. A semantic
overlay can change or suppress that permission without changing the segment.
Headroom cells provide open space through their effective area and capability
rules. They can use a different medium and do not need a support segment.

Use `ResolveSideViewAnchor` to obtain the capsule center above the canonical
support midpoint. The center can belong to a different cell from its support
owner. `ValidateSideViewLeg` checks two anchors and the entire upright capsule
sweep using the current publication and a finite work budget. It checks authored
support surfaces in the selected coverage cells as well as the cells themselves.
Exact tangency is allowed. A rounded center or an intermediate pose that
penetrates a surface fails the proof.

Collinear supports with an exact shared endpoint and a common fixed-point
normal use a straight leg. At a valley, the finite capsule-center tracks from
the two anchors toward the seam must intersect at an exact fixed-point
waypoint. Each stage must keep contact with its own support and clear the full
capsule sweep. A join whose intersection rounds off either track fails the
leg check.

Different-height horizontal supports may join at a shared X boundary in one
rectangular row. A step up lifts before crossing at
the higher center height. A drop crosses before descending. Each phase must
clear the capsule and stay within the pinned support owners' trace closure.
The profile supplies separate limits for rising and dropping. Ordinary leg
checks reject gaps, crests, and height discontinuities involving a sloped
support. Valid endpoint anchors and an allowed height difference are only
part of the proof.

For a horizontal step, the lower center must be at least one capsule radius
from the seam in X. This reserves clearance beside the step during the lift
or descent.

Use [side-view guides](Pathing.md#immediate-side-view-guides) to search these
ordinary legs with A* or Flow.

## Complete Cell Payloads

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

## Default And Explicit Entries

The optional map default applies to every physically present, in-bounds
GridForge cell that has no higher-precedence semantic cell. It does not create a
GridForge cell and does not fill an absent sparse address.

Explicit entries override the default. This makes both dense and sparse
authoring cheap:

- a Gas-default map can list only walls or special areas;
- a Liquid-default map can list islands or hazards;
- a map with no default is fail-closed except at explicit entries.

## Runtime Precedence

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

## Physical And Semantic Independence

GridForge owns whether an address exists, is blocked, and which exact prism and
contacts it has. Trailblazer owns the semantic payload admitted at that address.

Consequences:

- a default never materializes absent physical storage;
- a default-backed map remains fail-closed while its bounded physical baseline
  is captured across fixed frames;
- an authored semantic cell at an absent sparse address is dormant;
- a GridForge add/remove or blockage event can activate or deactivate graph
  state without rewriting the map;
- map and physical dependencies both participate in guide staleness.

## Medium-State Connectivity

Structural connectivity uses positive-face contact:

- rectangular prisms: six faces;
- pointy/flat hex prisms: six planar faces plus top and bottom.

The same physical cell may have Solid, Gas, and Liquid search states. Native
movement retains medium. Volume shortcuts improve route quality but never create
connectivity on their own. Semantic transitions are the only edges that can
change medium.

## Publication

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
`NavigationOverlayCommitOperation`. See [Map publication](MapPublication.md).

An applied commit receipt confirms the authored map was accepted. For a map with
a default cell, physical baseline materialization can continue afterward under
the context's maintenance budget. Do not query that map until its diagnostic
`IsMaterialized` value is `true`.

## Related guides

- [Map authoring](MapAuthoring.md)
- [Map publication](MapPublication.md)
- [Pathing](Pathing.md)
- [Volume traversal](VolumeTraversal.md)
- [Transitions](Transitions.md)
