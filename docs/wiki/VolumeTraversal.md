# Volume Traversal Reference

In Trailblazer, Volume means free-form deterministic travel through Gas or
Liquid matter. It is a medium-state property, not a terrain category, special
request type, or separate search system.

## 1. Core Model

A Gas or Liquid search state exists when all of these agree:

- the GridForge address physically exists and is not blocked;
- the effective `NavigationCell.Media` contains the exact medium;
- the agent's `AllowedMedia` contains it;
- required capabilities are satisfied;
- the area policy admits the cell;
- the body profile fits the required coverage.

The same `PathQuery`, A*, Flow, dependency, cache, and guide machinery used for
Solid traversal is used for Gas and Liquid.

## 2. Free-Form Geometry

Gas/Liquid movement uses a profile-resolved body anchor centered vertically in
the cell prism. It does not reuse Solid foot-anchor step/drop semantics.

GridForge owns:

- rectangular and pointy/flat hex direction sets;
- cell prisms and face contacts;
- directed portal traversal;
- all prisms positively overlapped by the swept upright body;
- exact validation through the selected prism union.

Rectangular grids can evaluate all 26 directions. Hex prisms can evaluate their
complete 20-direction set. Non-face candidates require closure and swept-union
proof, so shortcuts improve route quality without creating connectivity or
cutting corners.

Movement cost is the conservative ceiling of exact world-space centered-anchor
distance plus destination enter costs. There are no unit-grid straight/diagonal
constants and no floating-point distance path.

## 3. State Of Matter, Not Terrain

Trailblazer does not ask whether an address is cave, ocean, sky, room, or biome.
The host may use those facts while authoring, but it publishes a complete cell:

~~~csharp
NavigationCell liquid = new(
    TraversalMedia.Liquid,
    TraversalCapability.Swim,
    waterArea,
    enterCost: Fixed64.Zero,
    radiusClearance: Fixed64.One,
    heightClearance: Fixed64.One);
~~~

Terrain remains optional. A flight simulation can publish Gas without terrain;
a submarine map can publish Liquid; a mixed cell can support more than one
medium when that is physically meaningful.

## 4. Map Defaults

Map defaults make large uniform volumes cheap. A Gas-default map needs entries
only for exceptions. A flooded replacement can use a Liquid default without
rewriting every physical address.

~~~csharp
NavigationMap gasMap = new NavigationMapBuilder(mapId, binding)
    .SetDefaultCell(gasCell)
    .Build();

NavigationMap liquidMap = new NavigationMapBuilder(mapId, binding)
    .SetDefaultCell(liquidCell)
    .Build();
~~~

To flood the already published map, prepare `liquidMap` with a higher bake
version and admit it with
`OverlayReplacementPolicy.PreserveAndRevalidate`. The map ID and binding stay
the same; affected Gas proofs become stale and new Liquid queries can resolve.
The policy preserves the exact overlay/dynamic-address set; if that set is
incompatible with the Liquid bake, the whole replacement rejects rather than
pruning entries. Use `Clear` when flooding should discard every old overlay.

Important details:

- explicit baked cells still override the new default;
- a default does not create absent GridForge cells;
- a default replacement is immutable map publication, not a query toggle;
- drain/flood overlays remain appropriate when only addressed cells change.

## 5. Queries

A free-flight query is ordinary `PathQuery` intent:

~~~csharp
var query = new PathQuery(
    new NavigationEndpoint(startFoot, mapId),
    new NavigationEndpoint(endFoot, mapId),
    flyingProfile,
    policy.Key,
    new TraversalIntent(TraversalMedium.Gas, TraversalMedia.Gas),
    PathAlgorithm.AStar,
    budget,
    allowTransitions: false);
~~~

Use Liquid as the exact start medium for swimming. To permit a Liquid-to-Gas
takeoff, include Gas in `TargetMedia`, give the agent the required capabilities,
set `AllowTransitions` to true, and author a transition definition or rule.

## 6. Large Bodies And Cross-Grid Travel

When a body fits one directed portal, Trailblazer uses GridForge's fast path. A
larger body falls back to bounded swept-union coverage. Missing output capacity,
arithmetic overflow, stale grid identity, blocked required coverage, and a real
geometric rejection remain distinct failure paths.

Automatic seams can connect compatible map/grid faces. Heterogeneous or
misaligned geometry fails closed when GridForge cannot issue an exact directed
proof. Semantic actions do not substitute for missing physical movement unless
the host explicitly authors that action.

## 7. Dynamic Fluids

For an addressed flood or drain, publish complete cell overlay operations:

- Set Liquid/Gas/Solid payloads for the changed addresses;
- Suppress when no semantic navigation cell should exist;
- Revert when the bake/default should become visible again.

Materialize any fluid simulation result before publication and preserve stable
operation order. Search never calls the fluid simulation as a predicate.

## 8. Relationship To Transitions

Volume movement keeps the current medium. A transition is a semantic action that
may change it. Examples:

- Liquid to Gas Takeoff;
- Gas to Liquid Landing;
- Solid to Liquid SwimEntry;
- Liquid to Solid SwimExit;
- same-medium Gas teleport.

Media contact alone never generates these actions. See
[Transitions](Transitions.md).

## 9. Related References

- [Navigation maps](NavigationCharts.md)
- [Map authoring](ChartAuthoring.md)
- [Pathing](Pathing.md)
- [Path guides](PathGuides.md)
