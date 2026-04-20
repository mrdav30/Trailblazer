# Guided Climb Intent Recompute Plan

## Purpose

This document captures a narrow hardening slice for recomputing auto-derived guided climb intent
when `NavSteering` repaths into a different authored transition topology.

The goal is not to change explicit guided climb requests. The goal is to keep auto-derived climb
intent aligned with the current effective guided route when steering refreshes or repaths a
transition-aware request.

## Why This Still Matters

The current guided climb flow resolves intent in two places:

- when `Navigator.ApplyGuidedTrekRequest(...)` first creates a guided request
- when a pending `GuidedVolumeExitHandoff` activates its chart-backed follow-up request

That is enough for the first route shape, but it is not enough for later repaths.

`NavSteering` can rebuild the effective route without a new guided request being applied:

- it mutates the live `IPathRequest` origin during validation and repath
- it can request a new guide when unit size changes, when the path becomes invalid, or when stuck
  recovery schedules a retry
- transition-aware A* and flow-field requests can move between:
  - direct chart travel
  - chart routes that require authored climb transitions
  - chart routes that no longer require climb transitions

That means `_guidedClimbIntent` can become stale while the same guided request remains active.

There is also a closely related handoff case:

- a volume-first guided request can activate a pending liquid or gas exit handoff
- the handoff currently carries climb intent derived from the exit transition metadata
- the activated chart-backed follow-up can still resolve into a later authored climb topology that
  was not fully described by the exit transition alone

That means auto-derived climb intent can also be stale on the same frame the follow-up request
activates, even though the downstream route is already transition-aware.

## Current Gap

Today `Navigator` stores only one cached climb-intent value:

- `_guidedClimbIntent`

and `RefreshGuidedIntentState()` just mirrors that cached value into `_frameRequest`.

The current code does not distinguish:

- explicit host intent such as `ApplyGuidedTrekRequest(... isRequestingClimb: true/false)` or
  `ToggleGuidedClimb(...)`
- auto-derived intent inferred from the current transition-aware route topology

That distinction matters because only the auto-derived case should be recomputed on repath.

It also matters because `GuidedVolumeExitHandoff.IsRequestingClimb` is intentionally narrow:

- it is a lightweight exit-handoff hint
- it is not a full cache of the downstream chart route topology

That is a good design for the handoff object, but it means the steering-owned route topology still
needs to become authoritative once the chart follow-up is live.

## Recommendation

Keep explicit guided climb intent sticky.

Only recompute guided climb intent when:

1. the current guided request is using auto-derived climb intent
2. `NavSteering` has refreshed the effective route topology for the current request

Do not recompute by rebuilding hybrid topology every frame in `Navigator`.
That would be correct but unnecessarily expensive.

## Proposed Shape

### 1. Track guided climb intent mode explicitly

Recommended direction:

- add a small internal mode such as:

```csharp
internal enum GuidedClimbIntentMode
{
    Auto,
    Explicit
}
```

Behavior:

- `ApplyGuidedTrekRequest(... isRequestingClimb: true/false)` sets `Explicit`
- `ToggleGuidedClimb(...)` sets `Explicit`
- auto-derived initialization from route topology sets `Auto`
- clearing guided movement resets the mode to the default auto state

This prevents repath-driven recompute from clobbering explicit host intent.

### 2. Let `NavSteering` publish route-topology climb metadata

Recommended direction:

- add steering-owned metadata describing the current effective route’s climb-intent requirement
- update that metadata only when the route topology is refreshed

Suggested minimal surface:

- `bool CurrentRouteRequestsClimbIntent`
- `int CurrentRouteTopologyVersion`

`CurrentRouteTopologyVersion` should increment when the effective route meaningfully changes, such
as:

- a new request is applied
- a path refresh resolves to direct travel
- a path refresh resolves to a guide-backed route
- a transition-aware fallback rebuild changes the route shape
- movement arrives or stops and clears the route

This keeps the expensive topology inference in the subsystem that is already rebuilding the route.

### 3. Update climb-intent metadata at route-refresh time, not every frame

Recommended direction:

- when `NavSteering` refreshes a chart-backed transition-aware route, compute whether the effective
  route requests climb intent
- cache that result on the steering session

Important nuance:

- direct line-of-sight travel should publish `false`
- volume-first requests with pending handoffs should continue using the handoff-owned intent path
- chart-backed requests that rebuild into hybrid climb topology should publish `true` when any
  effective directed transition requests climb intent

This avoids repeated hybrid reconstruction on every `Navigator.Simulate()`.

### 4. Sync auto-derived intent after `GetHeading(...)`

This is the timing-critical part.

`NavSteering.GetHeading(...)` is where route refresh and repath actually happen. If `Navigator`
only recomputes on the next frame, the motor can still run one frame with stale climb intent.

Recommended direction:

- after `Steering.GetHeading(...)` runs, but before `NavMotor.TryTraversal(...)` runs, have
  `Navigator` compare the last-seen `CurrentRouteTopologyVersion`
- if the mode is `Auto` and the version changed, sync `_guidedClimbIntent` from
  `Steering.CurrentRouteRequestsClimbIntent`
- then mirror the resulting value into `_frameRequest.IsRequestingClimb`

This keeps the current frame aligned with the just-refreshed route.

### 5. Preserve existing handoff behavior

Do not replace the current `GuidedVolumeExitHandoff` path.

Keep:

- initial volume-leg climb intent resolution
- handoff-owned `IsRequestingClimb` on follow-up activation

After the follow-up request is active, route-topology recompute should apply normally if the follow-up
request is still in `Auto` mode.

Important nuance:

- the handoff should remain the bootstrap hint for the activation frame
- once `Steering.GetHeading(...)` resolves the active follow-up route topology, auto-derived intent
  should be allowed to sync from that route in the same frame
- this covers cases such as exiting liquid onto solid and then immediately continuing into a later
  authored climb chain

## Non-Goals

- changing explicit `isRequestingClimb` behavior
- recomputing climb intent every frame from scratch
- widening `NavSteering` into a climb locomotion subsystem
- changing the pathing fallback planner semantics
- changing the default transition metadata model

## Implementation Slice

### Phase 1. State Ownership

1. Add internal guided climb intent mode tracking in `Navigator`.
2. Add steering-owned route topology climb metadata and a topology version counter.
3. Reset both cleanly on `StopMove()`, `Arrive()`, and request replacement.

### Phase 2. Steering Integration

1. When route validation or repath refreshes the effective route, update:
   - `CurrentRouteRequestsClimbIntent`
   - `CurrentRouteTopologyVersion`
2. Publish `false` for direct travel and cleared routes.
3. Publish transition-derived intent only for the effective route actually chosen.

### Phase 3. Navigator Integration

1. Move or split guided-intent refresh so it runs after `Steering.GetHeading(...)`.
2. If the guided climb mode is `Auto` and the steering topology version changed, sync the cached
   guided climb intent from steering.
3. Preserve explicit intent unchanged.

### Phase 4. Coverage

Add focused tests that prove:

1. auto-derived guided climb intent updates when a transition-aware route repaths from non-climb
   to climb
2. auto-derived guided climb intent clears when a repath removes the climb-requiring transition
   topology
3. explicit guided climb intent remains unchanged across repath
4. handoff-driven guided climb intent still activates correctly before later auto recompute applies
5. a volume-exit handoff can activate into a chart follow-up whose downstream authored climb chain
   requires climb intent even when the exit transition itself was not the full reason that climb is
   needed

### Phase 5. Documentation

Update:

- `docs/NAVIGATOR.MD`
- `docs/NAVSTEERING.MD`

Document that auto-derived guided climb intent follows the current effective route topology, while
explicit guided climb intent remains host-owned and sticky.

## Acceptance Criteria

This hardening slice is complete when:

1. auto-derived guided climb intent stays aligned with the current effective guided route
2. explicit guided climb intent is never overwritten by repath
3. route-topology recompute does not require rebuilding hybrid intent every frame
4. the same simulation frame sees the updated auto-derived climb intent after route refresh
5. liquid-exit follow-up routes and normal chart repaths share the same auto-intent recompute rules
6. focused tests cover both auto and explicit intent paths
