---
uid: Trailblazer
summary: *content
---

Trailblazer provides deterministic, engine-agnostic navigation for lockstep
games and simulations. `TrailblazerWorldContext` owns navigation state for one
GridForge world, including fixed-step publication, graph generations, guide
services, controller coordination, and deterministic heightmaps. Its clock
exposes a long frame count and Chronicler wide elapsed timestamp; Fixed64
timesteps remain the motion-integration boundary.

The root namespace contains context, clock, lifecycle, logging, and traversal
medium contracts. Map authoring, queries, algorithms, and guide leases live in
`Trailblazer.Pathing`; optional controller services live in
`Trailblazer.Navigation`.

Trailblazer is the navigation and character-controller layer of the Lockstep
Simulation Framework. It builds on
[FixedMathSharp](https://github.com/mrdav30/FixedMathSharp),
[SwiftCollections](https://github.com/mrdav30/SwiftCollections),
[GridForge](https://github.com/mrdav30/GridForge), and
[Chronicler](https://github.com/mrdav30/Chronicler).
