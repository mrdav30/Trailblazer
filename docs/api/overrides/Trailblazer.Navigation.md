---
uid: Trailblazer.Navigation
summary: *content
---

Compose host-owned character controllers from `Navigator`, steering, movement
groups, occupancy, motor and turning services, heightmap grounding, and explicit
Chronicler records.

`Navigator2D` runs native top-down circle routes through a host-owned
prepare, resolve, and commit barrier. It supports immediate and scheduled
guidance, formation goals, and explicit host actions.

The optional `Trailblazer.Gravitas` assembly adds `GravitasNavigator3D` without
coupling the core libraries. Read the
[Gravitas integration guide](../guides/Gravitas.md) for shared-world ownership,
prepare/resolve/commit ordering, and supported shapes.

Navigator acquisition is Immediate by default. Opt-in Scheduled steering uses
the existing context queue and fair current-foot validation turns while ordinary
motor/gravity/platform and commit processing continues during waiting. A moved
A* origin rejoins its exact ordinary start anchor with an independently owned,
capacity-accounted proof, reproved for every connector heading. Acquisition-mode
changes preserve that adopted connector's validation obligation.

Read the [Navigator guide](../guides/Navigator.md) before integrating controller
lifecycle, the
[NavSteering guide](../guides/NavSteering.md#periodic-line-of-sight-rechecks)
for optional stable periodic direct-LOS alignment, and the
[serialization guide](../guides/Serialization.md) before restoring controller
state.
