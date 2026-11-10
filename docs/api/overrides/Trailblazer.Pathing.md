---
uid: Trailblazer.Pathing
summary: *content
---

Author immutable navigation maps, publish deterministic graph changes, describe
complete A* or Flow requests, and consume immutable guide leases in this
namespace.

`TrailblazerGuideService` supports immediate top-down route leases, synchronous
spatial acquisition and a context-owned scheduled FIFO. Both spatial acquisition
modes consume the same `PathQuery` and return the same guide leases.
Scheduled request ownership and fixed-frame work slices are explicit.
Compatible contiguous scheduled requests form frozen cohorts. Builds remain
oldest-first; covered followers can complete before uncovered members, with
independent lease state and endpoint proofs under existing retention ceilings.

Start with the [map-authoring](../guides/MapAuthoring.md),
[pathing](../guides/Pathing.md), and [path-guide](../guides/PathGuides.md)
guides for the lifecycle and ownership rules behind these APIs.
