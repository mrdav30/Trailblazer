---
uid: Trailblazer.Pathing
summary: *content
---

Author immutable navigation maps, publish deterministic graph changes, describe
complete A* or Flow requests, and consume immutable guide leases in this
namespace.

`TrailblazerGuideService` supports synchronous acquisition and a context-owned
scheduled FIFO. Both consume the same `PathQuery` and return the same guide
leases; scheduled request ownership and fixed-frame work slices are explicit.

Start with the
[map-authoring](https://github.com/mrdav30/Trailblazer/wiki/MapAuthoring),
[pathing](https://github.com/mrdav30/Trailblazer/wiki/Pathing), and
[path-guide](https://github.com/mrdav30/Trailblazer/wiki/PathGuides) guides for
the lifecycle and ownership rules behind these APIs.
