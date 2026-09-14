# Serialization

Trailblazer serializes explicit runtime records through Chronicler. The
standard `Trailblazer` package supports JSON and MemoryPack;
`Trailblazer.Lean` omits the MemoryPack transport. Both package families use a
populate-existing-instance-only load model.

## Load Model

The host must:

1. create or attach the correct `TrailblazerWorldContext`;
2. restore GridForge grids;
3. publish every referenced `NavigationMap` and `NavigationAreaPolicy`;
4. replay persisted overlay transactions in deterministic order;
5. restore any host-owned physical body with geometry matching the recorded
   profile and position;
6. create/setup the Navigator shell with the exact recorded profile;
7. populate the existing shell through Chronicler;
8. resume fixed-step simulation so fresh guidance can be acquired.

Trailblazer does not construct arbitrary runtime/controller graphs from save data
and does not serialize context bindings.

## Standalone PathQuery

`PathQueryRecord` is the public standalone record for one complete immutable
query. This C# fragment assumes a validated query and a chosen Chronicler
transport:

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

## Navigator Path Session

Navigator stores a different durable session shape. It preserves destination,
map filters, endpoint policies, target media, area policy, algorithm, budget,
Flow options, and transition permission. On load it rebuilds:

- start position from the restored Navigator foot position;
- start medium from the restored `TrekCondition.Medium`;
- agent profile from the already configured shell.

This distinction is intentional. A resumed moving controller must start from its
restored physical state, while a standalone `PathQueryRecord` is an exact value
round trip.

The outer Navigator record is schema version 6. The nested path-session and
standalone `PathQueryRecord` schemas remain version 1. Version 6 retains the
earlier explicit world-space ground `SurfaceNormal` and controlled-descent
request, configuration, and active fall state. It adds the periodic direct-LOS
interval and nullable phase. Earlier outer Navigator schemas reject
transactionally rather than supplying a derived or default contract through a
compatibility path.

Controlled descent restores only when its loaded fall and motor state can admit
it. Loading clears the active flag when the agent is not falling in Gas, when
flight, climb, or swim is active, or when controlled descent is disabled or has
non-positive terminal-speed/deceleration tuning. This normalization does not
discard the ordinary fall start/end state.

Navigator does not serialize or restore:

- an A* or Flow payload lease;
- a guide cursor/sample source;
- a pending transition instruction;
- private completion stamps;
- dependency snapshots or cache slots;
- the context logical clock, clock lifetime, or a phased due marker;
- `LastCommittedCell` or committed-cell notifications.

With a configured phase, restore discards serialized direct-LOS trust and
requires a fresh admission decision on the next eligible steering update,
whether loading into the same context or a new one. It does not acquire a guide
during population. With no phase, the established direct/countdown restore
behavior remains unchanged. Navigator population does not restore or advance
the context clock.

## Transactional Load

Navigator loading stages and validates the top-level schema, exact profile,
position/frame condition, path session, steering, turning, motor, and locomotion
records before changing the existing live shell.

A successful runtime profile replacement becomes the Navigator's current
serialized profile. Population still requires a shell configured with that
exact profile; load does not call the runtime physical-replacement hook or
resize a host collider. Physical adapters such as `GravitasNavigator3D` must
therefore restore the matching body shape and accepted root before population.
This keeps the host body and navigation envelope consistent before the next
fixed frame.

Malformed early or late nested data must preserve the previous shell, active
query, guide lease, pending instruction, LOS configuration, and timing state.
This applies to JSON in both package families and to MemoryPack in the standard
package. A failed load does not partially stop the current session, replace
controller tuning, or release guidance. It also preserves the shell's existing
GridForge occupancy registration and committed-cell state.

After all staged data validates, population removes the shell's old occupancy
registration before applying the restored identity/position, registers the new
position exactly once, and silently rebuilds `LastCommittedCell` against the
already published graph. No committed-cell callback is replayed during load.

Missing or retired schema shapes reject explicitly. Chronicler's missing-field
defaults are not used as a compatibility path for old navigation request/action
formats.

## Current Navigator Coverage

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

## Maps And Overlays

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

## Pending Actions

Pending transition instructions are deliberately transient and lease-specific.
Saving during a held action records durable query/controller state but not the
action token. On load:

- pending is cleared;
- no completion is replayed;
- no guide is reacquired during population;
- the next fixed-step request resolves against current published world state.

The host should separately persist its own gameplay/animation state if an action
must resume at an application level.

## Canonical Defaults

`RecordValues.Look(...)` defaults are canonical field defaults, not whatever
value happens to be in the target shell. Omitted values load as those defaults.
Reference records use `RecordDeep`; non-nullable structs use
`RecordDeepStruct`; optional structs use `RecordNullableDeep`.

This behavior is one reason schema validation is mandatory at the navigation
boundary.

## What Is Not Serialized

- `TrailblazerWorldContext` and `GridWorld` ownership;
- navigation map/overlay/policy publication;
- immutable graph snapshots and caches;
- active guide leases, cursors, pending actions, or completion stamps;
- last committed cell metadata and host callbacks;
- movement-group coordinator state (group intent is recorded and can be
  prewarmed);
- heightmap sample data/layer registration;
- engine objects, physics state, animation state, or host callbacks.

Scheduled guide requests, their FIFO position, continuations and unclaimed
results are runtime-only. Record host query intent if it must be replayed;
restore maps/policies first, then resubmit that intent in deterministic order.
Do not serialize `NavigationGuideRequest` or assume a reset preserves pending
work. See
[scheduled acquisition](Pathing.md#spreading-acquisition-across-fixed-frames).

## Related guides

- [Overview](Overview.md)
- [Map publication](MapPublication.md)
- [Pathing](Pathing.md)
- [Navigator](Navigator.md)
