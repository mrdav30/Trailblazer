---
title: Trailblazer API
description: API reference and behavioral guides for deterministic navigation and character control.
---

<div class="trb-hero">
  <p class="trb-kicker">LOCKSTEP NAVIGATION FOR .NET</p>
  <h1>Give every agent the same way forward.</h1>
  <p>Trailblazer combines immutable GridForge-backed maps, deterministic A* and
  flow fields, explicit traversal actions, controller services, and
  fixed-point heightmaps inside world-owned contexts.</p>
  <div class="trb-actions">
    <a href="https://github.com/mrdav30/Trailblazer/wiki/GettingStarted">Get started</a>
    <a href="xref:Trailblazer">Browse the API</a>
  </div>
</div>

## Build and publish navigation truth

<div class="trb-card-grid">
  <div class="trb-card">
    <h3><a href="xref:Trailblazer.TrailblazerWorldContext">Own the world</a></h3>
    <p>Keep the clock, GridForge binding, pathing state, guide services, and
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
    <p>Consume immutable-payload leases with acquisition-local cursors and
    completion-safe action instructions.</p>
  </div>
  <div class="trb-card">
    <h3><a href="xref:Trailblazer.Navigation.Navigator">Compose a controller</a></h3>
    <p>Bring steering, turning, motor, locomotion, occupancy, heightmap
    grounding, and explicit serialization together when your host needs them.</p>
  </div>
</div>

## Package family

| Package            | Serialization profile                                           |
| ------------------ | --------------------------------------------------------------- |
| `Trailblazer`      | Standard LSF dependencies with JSON and MemoryPack support      |
| `Trailblazer.Lean` | Lean LSF dependencies and JSON without the MemoryPack transport |

Use one package family throughout the complete LSF dependency graph.

## Part of the LSF stack

Trailblazer builds navigation and controller policy on focused lower layers:

- [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp) for deterministic
  fixed-point math, transforms, and geometry.
- [SwiftCollections](https://github.com/mrdav30/SwiftCollections) for
  low-allocation collections, pools, and retained storage.
- [GridForge](https://github.com/mrdav30/GridForge) for world topology, physical
  cell identity, prisms, contacts, and geometry proofs.
- [Chronicler](https://github.com/mrdav30/Chronicler) for explicit state
  transfer, JSON, MemoryPack, and record schemas.

## Resources

- [Behavioral guides and getting started](https://github.com/mrdav30/Trailblazer/wiki)
- [v1 to v2 migration guide](https://github.com/mrdav30/Trailblazer/blob/main/docs/MIGRATION.md)
- [Source, issues, and releases](https://github.com/mrdav30/Trailblazer)
- [Core test-suite coverage](https://mrdav30.github.io/Trailblazer/coverage/)

The API reference is generated from Trailblazer's XML documentation. The wiki
explains world ownership, authoring, publication, pathing, guides, transitions,
controllers, serialization, and troubleshooting in task-oriented prose.
