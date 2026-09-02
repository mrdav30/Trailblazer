# Trailblazer Wiki

Welcome. Trailblazer is deterministic, engine-agnostic navigation for lockstep
games and simulations. It combines GridForge-backed navigation maps, A* and flow
fields, explicit traversal actions, steering, turning, locomotion, and
serialization without taking ownership of your engine or gameplay systems.

Use this wiki when you want to understand how the pieces behave together. The
[public API reference](https://mrdav30.github.io/Trailblazer/api/Trailblazer.html)
defines individual signatures; this wiki explains how those APIs behave
together.

## Start here

New to Trailblazer? Follow this path:

1. [Getting Started](GettingStarted.md) — install a package, publish a tiny
   world, and request your first route.
2. [Technical Overview](Overview.md) — learn what Trailblazer owns and what the
   host still controls.
3. [Navigation Maps](NavigationMaps.md) — understand physical cells, navigation
   meaning, defaults, and overlays.
4. [Pathing](Pathing.md) and [Path Guides](PathGuides.md) — build queries and
   consume A* or Flow results safely.

## Find the right guide

| I want to...                                     | Read...                                                       |
| ------------------------------------------------ | ------------------------------------------------------------- |
| Build or import navigation data                  | [Map Authoring](MapAuthoring.md)                              |
| Publish maps, policies, or runtime changes       | [Map Publication](MapPublication.md)                          |
| Route through Gas or Liquid                      | [Volume Traversal](VolumeTraversal.md)                        |
| Add ladders, jumps, takeoff, or teleporters      | [Transitions](Transitions.md)                                 |
| Drive a complete controller                      | [Navigator](Navigator.md)                                     |
| Customize steering or facing                     | [NavSteering](NavSteering.md) and [NavTurning](NavTurning.md) |
| Configure movement, jumping, water, or platforms | [NavMotor](NavMotor.md) and [Gravity](Gravity.md)             |
| Use deterministic ground-height data             | [Heightmaps](HeightMaps.md)                                   |
| Save and restore runtime state                   | [Serialization](Serialization.md)                             |
| Diagnose a failed query or stuck action          | [Troubleshooting](Troubleshooting.md)                         |

## The mental model

Most Trailblazer integrations follow the same lifecycle:

1. Create a `TrailblazerWorldContext` for one GridForge world.
2. Publish immutable maps and exact area policies.
3. Advance publication once per fixed simulation frame.
4. Request an A* route or Flow field with one immutable `PathQuery`.
5. Move through ordinary guidance and execute semantic actions explicitly.
6. Dispose leases, Navigators, and then the context.

Trailblazer owns deterministic navigation meaning. Your host owns terrain
classification, collision detection, physics, animation, and gameplay actions.

## Project links

- [GitHub repository](https://github.com/mrdav30/Trailblazer)
- [Trailblazer on NuGet](https://www.nuget.org/packages/Trailblazer)
- [Trailblazer.Lean on NuGet](https://www.nuget.org/packages/Trailblazer.Lean)
- [API reference](https://mrdav30.github.io/Trailblazer/api/Trailblazer.html)
- [Coverage report](https://mrdav30.github.io/Trailblazer/coverage/)
- [v1 to v2 migration guide](../MIGRATION.md)

For contributor workflow and repository boundaries, see
[AGENTS.md](../../AGENTS.md).
