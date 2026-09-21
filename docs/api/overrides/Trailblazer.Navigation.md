---
uid: Trailblazer.Navigation
summary: *content
---

Compose host-owned character controllers from `Navigator`, steering, movement
groups, occupancy, motor and turning services, heightmap grounding, and explicit
Chronicler records.

The optional `Trailblazer.Gravitas` assembly adds `GravitasNavigator3D` without
coupling the core libraries. Read the
[Gravitas integration guide](https://github.com/mrdav30/Trailblazer/wiki/Gravitas)
for shared-world ownership, prepare/resolve/commit ordering, and supported shapes.

Navigator acquisition is Immediate by default. Opt-in Scheduled steering uses
the existing context queue and fair current-foot validation turns while ordinary
motor/gravity/platform and commit processing continues during waiting. A moved
A* origin rejoins its exact ordinary start anchor with an independently owned,
capacity-accounted proof, reproved for every connector heading. Acquisition-mode
changes preserve that adopted connector's validation obligation.

Read the
[Navigator guide](https://github.com/mrdav30/Trailblazer/wiki/Navigator) before
integrating controller lifecycle, the
[NavSteering guide](https://github.com/mrdav30/Trailblazer/wiki/NavSteering#periodic-line-of-sight-rechecks)
for optional stable periodic direct-LOS alignment, and the
[serialization guide](https://github.com/mrdav30/Trailblazer/wiki/Serialization)
before restoring controller state.
