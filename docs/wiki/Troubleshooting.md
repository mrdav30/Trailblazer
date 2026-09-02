# Troubleshooting

Most Trailblazer failures are explicit. Start with the returned status or
operation receipt, then check the ownership boundary that produced it.

## My admitted map or overlay is not visible

`context.Pathing.Admit(...)` only places an operation into bounded storage. It
does not publish the operation immediately.

Check all three steps:

1. `Admit(...)` returned `true`.
2. The host called `context.Simulate()` on later fixed frames.
3. The operation receipt reached `NavigationOperationStatus.Applied`.

A terminal rejected receipt means the candidate did not become partially
visible. Inspect its rejection status and fix the complete operation.

## A query returns `NoMap`

The endpoint map filter must match a currently published map. Confirm that:

- the map operation reached `Applied`;
- the endpoint uses the stable map ID, not a GridForge storage slot;
- the map is bound to the active context's GridWorld;
- a replacement or removal did not retire it.

If the map has a default cell, an `Applied` receipt can precede completion of
its bounded physical baseline capture. Trailblazer deliberately keeps the map
fail-closed during that work. Continue advancing fixed frames and inspect the
matching map's `IsMaterialized` value from
`context.Pathing.GetNavigationGraphDiagnostics()` before retrying the query.

## A query returns `InvalidProfile`

The agent profile must contain valid body geometry, arrival distance, media,
and movement limits. `TraversalIntent.TargetMedia` must be a nonempty subset of
the profile's `AllowedMedia`.

For `Navigator`, the query profile must exactly match the profile supplied at
setup. Trailblazer does not silently replace one with the other.

## A query returns `InvalidStart` or `InvalidEnd`

Endpoint resolution checks more than position. The address must be physically
present, have an effective navigation cell, admit the requested medium, fit the
body profile, satisfy capabilities, and pass the exact area policy.

Use `EndpointResolutionPolicy.NearestNavigable` only when nearby resolution is
part of the intended behavior, and always provide a finite maximum distance.
It does not make an otherwise illegal cell traversable.

## A query returns `NoPath`

`NoPath` is a completed graph result, not a capacity error. Common causes are:

- the start and target states are disconnected;
- a required transition was not authored;
- `AllowTransitions` is false;
- the agent lacks a required capability;
- the target-media mask excludes the reachable destination medium;
- the area policy blocks a necessary cell;
- a sparse GridForge address does not physically exist;
- clearance or portal geometry rejects the body profile.

Touching Gas, Liquid, or Solid cells does not create a semantic action by
itself. Author a definition or bounded rule when the route requires one.

## A query returns `BudgetExceeded`

The supplied `NavigationWorkBudget` was too small for the exact work performed.
Increase only the exhausted category, then measure again. A zero counter
disables that category; no counter grows automatically.

`BudgetExceeded` is different from `CapacityExceeded`: a budget belongs to the
request, while capacity belongs to fixed context storage or active lease
pressure.

## A query returns `CapacityExceeded`

Release disposable guide and graph leases promptly. If the failure persists,
compare the workload with `TrailblazerWorldContextSettings` and its operation,
query, snapshot, and retained-state limits.

For a held transition, transient capacity pressure is retryable. Keep the
instruction and retry completion on a later frame without performing the host
action twice.

## A guide becomes `Stale`

The guide depended on navigation truth that changed. Relevant map, overlay,
policy, seam, transition-rule, or GridForge publication invalidates the proof.
Dispose the stale lease and reacquire from current intent.

Do not keep moving from cached steps after `Stale`. Unrelated publication can
preserve a guide, but that decision belongs to the dependency system.

## A transition stays pending

A transition is an action barrier. The host must:

1. approach the instruction's exact source position;
2. perform the animation, physics, or gameplay action once;
3. complete the exact instruction from the active lease;
4. update physical position and traversal medium to its destination state.

Reconstructed, copied-from-another-lease, duplicate, or stale instructions do
not advance the cursor. `TryAdvanceStep()` cannot cross an action.

## Navigator does not resume correctly after loading

Restore in dependency order:

1. GridForge grids;
2. navigation maps and area policies;
3. persisted overlays;
4. the initialized Navigator shell;
5. the Navigator record.

Guide payloads, cursors, pending actions, context bindings, and committed-cell
notifications are not serialized. Fresh guidance is acquired on a later
simulation frame.

## Standard and Lean packages conflict

Use one dependency family throughout the LSF stack. Pair `Trailblazer` with
standard FixedMathSharp, SwiftCollections, GridForge, and Chronicler packages.
Pair `Trailblazer.Lean` with their Lean variants.

Restore the intended build configuration before using `--no-restore`:

```bash
dotnet restore Trailblazer.slnx --property:Configuration=Release
dotnet build Trailblazer.slnx --configuration Release --no-restore
```

Repeat both commands with `ReleaseLean` for the Lean family. Reusing restore
assets from the other configuration can make valid transport APIs appear to be
missing.

## Still stuck?

When reporting a problem, include:

- the exact status or receipt rejection;
- the query's endpoint, profile, media, algorithm, and budget;
- map ID and publication version;
- whether the failure reproduces in `Release` or `ReleaseLean`;
- the smallest deterministic world setup that reproduces it.

Related guides: [Map Publication](MapPublication.md), [Pathing](Pathing.md),
[Path Guides](PathGuides.md), [Transitions](Transitions.md), and
[Serialization](Serialization.md).
