# Serialization Reference

Trailblazer serializes explicit runtime records through Chronicler. The active
transports are JSON and MemoryPack, and the load model is
populate-existing-instance only.

## 1. Load Model

The host must:

1. create or attach the correct `TrailblazerWorldContext`;
2. restore GridForge grids;
3. publish every referenced `NavigationMap` and `NavigationAreaPolicy`;
4. replay persisted overlay transactions in deterministic order;
5. create/setup the Navigator shell with the exact recorded profile;
6. populate the existing shell through Chronicler;
7. resume fixed-step simulation so fresh guidance can be acquired.

Trailblazer does not construct arbitrary runtime/controller graphs from save data
and does not serialize context bindings.

## 2. Standalone PathQuery

`PathQueryRecord` is the public standalone record for one complete immutable
query:

~~~csharp
var record = new PathQueryRecord(query);
// Serialize record with JsonRecordSerializer or MemoryPackRecordSerializer.
~~~

It round-trips:

- exact start and end endpoints;
- exact agent profile/body shape;
- area-policy key/revision;
- exact start medium and target-media mask;
- A* or Flow algorithm and Flow options;
- every work-budget counter;
- transition permission.

A standalone query retains its exact recorded start position and start medium.
The record has a required schema and required query; a missing, malformed, or
unsupported record rejects rather than producing a default query.

## 3. Navigator Path Session

Navigator stores a different durable session shape. It preserves destination,
map filters, endpoint policies, target media, area policy, algorithm, budget,
Flow options, and transition permission. On load it rebuilds:

- start position from the restored Navigator foot position;
- start medium from the restored `TrekCondition.Medium`;
- agent profile from the already configured shell.

This distinction is intentional. A resumed moving controller must start from its
restored physical state, while a standalone `PathQueryRecord` is an exact value
round trip.

Navigator does not serialize or restore:

- an A* or Flow payload lease;
- a guide cursor/sample source;
- a pending transition instruction;
- private completion stamps;
- dependency snapshots or cache slots.

The next simulation frame requests fresh guidance from durable intent.

## 4. Transactional Load

Navigator loading stages and validates the top-level schema, exact profile,
position/frame condition, path session, steering, turning, motor, and locomotion
records before changing the existing live shell.

Malformed early or late nested data in either JSON or MemoryPack must preserve
the previous shell, active query, guide lease, and pending instruction. A failed
load does not partially stop the current session, replace controller tuning, or
release guidance.

Missing or retired schema shapes reject explicitly. Chronicler's missing-field
defaults are not used as a compatibility path for old navigation request/action
formats.

## 5. Current Navigator Coverage

The recorded Navigator branch includes:

- transform, velocity, speed, acceleration, and stable identity;
- exact `NavigationAgentProfile`;
- current frame condition/request;
- durable path-session intent;
- steering settings/session state;
- turning settings/state;
- motor and built-in locomotion state;
- heightmap-grounding settings;
- occupancy group identity.

Context-owned world data is separate.

## 6. Maps And Overlays

`NavigationMap` bakes and runtime overlay events are host/world assets, not
embedded inside Navigator records. Persist them in the host's deterministic world
snapshot/event log:

- stable map ID and bake version;
- GridForge configuration/storage state;
- map replacement policy;
- area-policy revisions;
- overlay operation sequence/effective frame and content.

Replay these before guided controller restoration. Otherwise a durable query may
correctly reject with `NoMap`, invalid policy, or stale dependencies.

## 7. Pending Actions

Pending transition instructions are deliberately transient and lease-specific.
Saving during a held action records durable query/controller state but not the
action token. On load:

- pending is cleared;
- no completion is replayed;
- no guide is reacquired during population;
- the next fixed-step request resolves against current published world state.

The host should separately persist its own gameplay/animation state if an action
must resume at an application level.

## 8. Canonical Defaults

`RecordValues.Look(...)` defaults are canonical field defaults, not whatever
value happens to be in the target shell. Omitted values load as those defaults.
Reference records use `RecordDeep`; non-nullable structs use
`RecordDeepStruct`; optional structs use `RecordNullableDeep`.

This behavior is one reason schema validation is mandatory at the navigation
boundary.

## 9. What Is Not Serialized

- `TrailblazerWorldContext` and `GridWorld` ownership;
- navigation map/overlay/policy publication;
- immutable graph snapshots and caches;
- active guide leases, cursors, pending actions, or completion stamps;
- movement-group coordinator state (group intent is recorded and can be
  prewarmed);
- heightmap sample data/layer registration;
- engine objects, physics state, animation state, or host callbacks.

## 10. Related References

- [Overview](Overview.md)
- [Runtime publication](PathManager.md)
- [Pathing](Pathing.md)
- [Navigator](Navigator.md)
