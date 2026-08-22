# Runtime Map Publication

`TrailblazerWorldContext.Pathing` is the public admission surface for unified
graph state. Maps, overlays, removals, area policies, and GridForge events are
composed into immutable snapshots at deterministic fixed-step boundaries.

## 1. Operation Types

| Operation | Purpose |
| --- | --- |
| `NavigationMapCommitOperation` | Install or replace one prepared immutable map |
| `NavigationMapRemoveOperation` | Remove one stable map ID |
| `NavigationOverlayCommitOperation` | Apply one atomic multi-map semantic overlay transaction |
| `NavigationAreaPolicyCommitOperation` | Publish one exact policy revision |

Every operation has a unique host-supplied sequence and earliest eligible
`EffectiveFrame`. Map, removal, and overlay operations call the sequence
`OperationSequence`; area-policy operations call it `PublicationSequence`.
All four operation kinds expose a receipt that starts pending and becomes
terminal. Its `PublishedFrame` records the actual publication frame.

## 2. Map Commit

This C# fragment assumes a prepared map and deterministic host sequence:

```csharp
var prepared = new PreparedNavigationMap(map, bakeVersion: 4);
var commit = new NavigationMapCommitOperation(
    prepared,
    OverlayReplacementPolicy.PreserveAndRevalidate,
    operationSequence: 100,
    effectiveFrame: context.FrameCount + 1);

bool admitted = context.Pathing.Admit(commit);
```

`Admit(...) == true` means the descriptor entered bounded operation storage. It
does not mean publication has completed. Observe `commit.Receipt.Status` after
advancing the context. A rejected or invalid candidate never partially changes
the live snapshot.

For a replacement of the same map ID:

- the GridForge binding must remain compatible;
- the bake version must advance;
- `PreserveAndRevalidate` preserves the exact overlay/dynamic-address set and
  validates the complete recomposed candidate;
- `Clear` atomically removes overlay state.

Preservation never prunes invalid entries. If the preserved set is incompatible
with the replacement bake, the complete operation rejects transactionally.

## 3. Overlay Transaction

One `NavigationOverlayTransaction` contains canonically ordered
`NavigationMapOverlayDelta` values. Each map delta can contain:

- cell Set/Suppress/Revert operations;
- physical connection Upsert/Suppress/Revert operations;
- explicit transition Upsert/Suppress/Revert operations.

The following C# fragment publishes one addressed cell change and a
bidirectional ladder atomically:

```csharp
var delta = new NavigationMapOverlayDelta(
    mapId,
    cells: new[]
    {
        NavigationCellOverlayOperation.Set(index, floodedCell)
    },
    transitions: new[]
    {
        TraversalTransitionOverlayOperation.Upsert(ladderDown),
        TraversalTransitionOverlayOperation.Upsert(ladderUp)
    });

var operation = new NavigationOverlayCommitOperation(
    new PreparedNavigationOverlay(
        new NavigationOverlayTransaction(new[] { delta })),
    operationSequence,
    context.FrameCount + 1);

context.Pathing.Admit(operation);
```

All deltas publish or reject together. This is the correct boundary for a ladder
whose two directions must appear atomically, or a flood that changes several
addresses as one simulation event.

## 4. Effective Cell Semantics

Cell precedence is overlay, explicit bake, map default, then no cell. Set carries
one complete payload. Suppress hides every lower layer. Revert removes the
overlay decision and reveals the bake/default.

Changing a map default requires a map replacement. A default is deliberately
not an overlay-wide mutable flag because it is immutable authoring truth.

## 5. Host Materialization

Terrain, fluids, doors, damage, construction, and other gameplay systems remain
host owned. Before admission, the host translates their deterministic result to
map or overlay data. This gives every change an address, operation sequence,
effective frame, receipt, and dependency footprint.

Do not install a search-time predicate for matter or terrain. A delegate can
change invisibly and would force global invalidation. Materialized cell data
lets the graph invalidate only affected pages/components when possible.

## 6. GridForge Changes

The context observes committed changes from its bound `GridWorld` and queues
them into the same graph-maintenance authority. `context.Simulate()` advances
that work together with eligible semantic publication. Hosts do not call a
separate pathing flush.

Physical add/remove/blockage and grid lifecycle changes are composed before
semantic overlay refresh for the same publication boundary.

An absent sparse cell remains absent even under a map default. If it later
appears, the effective semantic cell becomes active through the normal physical
event path.

## 7. Cache And Lease Effects

Published graph snapshots are immutable. A guide or cached result records the
exact pages, components, policy revision, transition-rule generation, and world
sequence it used. Relevant changes make that proof stale; unrelated pages can
remain reusable.

Held transition instructions are pull-validated. If their definition moves or
is removed, completion returns `Stale` and does not advance the cursor. The
controller can then release/reacquire guidance through its ordinary lifecycle.

## 8. Operation Limits

`NavigationOperationLimits` bounds queued operations, descriptor bytes,
retained map/overlay state, and resumable maintenance. Admission failure is
explicit. Publication work is metered and may span fixed steps without exposing
a partially composed graph.

Transition-rule publication is bounded independently by
`MaxTransitionRulesPerMap` and `MaxTransitionRules`. The builder validates rule
payloads and local duplicates; these public operation limits govern the
per-map and context-wide candidate-retained or published rule counts during
fold and publication.

## 9. Lifecycle Checklist

1. Add or restore GridForge grids.
2. Attach/create the `TrailblazerWorldContext`.
3. Publish every referenced map and area policy.
4. Replay persisted overlay transactions in deterministic order.
5. Flush/pump fixed-step publication until receipts are terminal.
6. Restore or start guided Navigators.
7. Dispose the context only after leases and host controllers are released.

## 10. Related References

- [Navigation maps](NavigationMaps.md)
- [Map authoring](MapAuthoring.md)
- [Pathing](Pathing.md)
- [Transitions](Transitions.md)
