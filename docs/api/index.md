---
title: Trailblazer
description: API reference and behavioral guides for deterministic navigation and character control.
---

<div class="trb-hero">
  <p class="trb-kicker">LOCKSTEP NAVIGATION FOR .NET</p>
  <h1>Give every agent the same way forward.</h1>
  <p>Trailblazer combines immutable GridForge-backed maps, deterministic A* and
  flow fields, explicit traversal actions, controller services, and
  fixed-point heightmaps inside world-owned contexts.</p>
  <div class="trb-actions">
    <a href="guides/GettingStarted.md">Get started</a>
    <a href="xref:Trailblazer">Browse the API</a>
  </div>
</div>

## Build and publish navigation truth

<div class="trb-card-grid">
  <div class="trb-card">
    <h3><a href="xref:Trailblazer.TrailblazerWorldContext">Own the world</a></h3>
    <p>Keep long-lived fixed-step timing, GridForge binding, pathing state, guide services, and
    heightmaps isolated inside one explicit context.</p>
  </div>
  <div class="trb-card">
    <h3><a href="xref:Trailblazer.Pathing.NavigationMapBuilder">Author immutable maps</a></h3>
    <p>Give physical cells complete navigation meaning, connections, and
    semantic transitions in canonical order.</p>
  </div>
  <div class="trb-card">
    <h3><a href="xref:Trailblazer.Pathing.TrailblazerPathingService">Publish deterministic changes</a></h3>
    <p>Admit maps, policies, removals, and overlays for fixed-step application
    without search-time callbacks.</p>
  </div>
</div>

## Query, guide, and move

<div class="trb-card-grid">
  <div class="trb-card">
    <h3><a href="xref:Trailblazer.Pathing.PathQuery">Describe one exact request</a></h3>
    <p>Choose endpoints, body geometry, traversal media, area policy, algorithm,
    finite work budget, and action permission explicitly.</p>
  </div>
  <div class="trb-card">
    <h3><a href="xref:Trailblazer.Pathing.TrailblazerGuideService">Acquire A* or Flow guidance</a></h3>
    <p>Acquire synchronously or through a deterministic frame-spread queue.
    Consume spatial leases with completion-safe actions or immediate top-down
    route leases with fixed-point cell centers.</p>
  </div>
  <div class="trb-card">
    <h3><a href="xref:Trailblazer.Navigation.Navigator">Compose a controller</a></h3>
    <p>Bring steering, turning, motor, locomotion, occupancy, heightmap
    grounding, opt-in scheduled acquisition with current-foot adoption,
    explicit periodic direct-LOS timing, and serialization together
    when your host needs them. Follow native top-down circle routes through
    your host's motion resolver with <a href="xref:Trailblazer.Navigation.Navigator2D">Navigator2D</a>.</p>
  </div>
</div>

## Package family

| Package                     | Serialization profile                                            |
| --------------------------- | ---------------------------------------------------------------- |
| `Trailblazer`               | Standard LSF dependencies with JSON and MemoryPack support       |
| `Trailblazer.Lean`          | Lean LSF dependencies and JSON without the MemoryPack transport  |
| `Trailblazer.Gravitas`      | Optional Gravitas-backed 3D Navigator with standard dependencies |
| `Trailblazer.Gravitas.Lean` | The same optional adapter with Lean dependencies                 |

Use one package family throughout the complete LSF dependency graph.

The optional adapter commits collision-accepted motion through an explicit host
loop. [Gravitas integration guide](guides/Gravitas.md),
<xref:Trailblazer.Navigation.GravitasNavigator3D>.

## Part of the LSF ecosystem

Trailblazer provides navigation and character control in the Lockstep Simulation
Framework.

- Deterministic fixed-point math, transforms, and geometry.
  [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp)
- Low-allocation collections, pools, and retained storage.
  [SwiftCollections](https://github.com/mrdav30/SwiftCollections)
- World topology, physical cell identity, and geometry proofs.
  [GridForge](https://github.com/mrdav30/GridForge)
- Explicit state transfer, record schemas, and wide simulation timing.
  [Chronicler](https://github.com/mrdav30/Chronicler)
- Deterministic physics for the optional 3D Navigator adapter.
  [Gravitas](https://github.com/mrdav30/Gravitas)

## Resources

- [Behavioral guides](guides/index.md)
- [Migration guide](https://github.com/mrdav30/Trailblazer/blob/main/docs/MIGRATION.md)
- [Source, issues, and releases](https://github.com/mrdav30/Trailblazer)
- [Core and adapter test coverage](https://mrdav30.github.io/Trailblazer/coverage/)
