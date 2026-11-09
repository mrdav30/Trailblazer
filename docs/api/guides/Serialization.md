# Serialization

Trailblazer serializes explicit runtime records through Chronicler. The standard
`Trailblazer` package supports JSON and MemoryPack; `Trailblazer.Lean` omits the
MemoryPack transport. Both package families use a
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

Trailblazer does not construct arbitrary runtime/controller graphs from save
data and does not serialize context bindings.

## Standalone PathQuery

`PathQueryRecord` is the public standalone record for one complete immutable
query. This C# fragment assumes a validated query and uses JSON:

```csharp
using Chronicler.Serialization;
using Trailblazer.Pathing;

var record = new PathQueryRecord(query);
string json = JsonRecordSerializer.Serialize(record);
```

Chronicler v1.0.0 places transports and payload editing in
`Chronicler.Serialization`; recording contracts and helpers remain in
`Chronicler`. Use `MemoryPackRecordSerializer` for binary payloads in the
standard package. See the
[Chronicler migration guide](https://github.com/mrdav30/Chronicler/blob/main/docs/MIGRATION.md)
for namespace changes and typed payload-editor replacements.

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

The outer Navigator record is schema version 9. The nested path-session and
standalone `PathQueryRecord` schemas are version 1. Navigator records include
the explicit world-space ground `SurfaceNormal`, controlled-descent state,
periodic direct-LOS interval and nullable phase, durable acquisition policy, and
active/buffered turn continuation.

Platform locomotion records the exact height-adjusted world attachment point
when it was sampled against the current carrier pose. A recorded point must
match the active carrier and the local attachment. Older version-9 records
without this optional point use the local attachment until the next accepted
sample establishes the exact point.

`JumpLocomotion` has its own required version-1 schema. `JumpStartTime` is a
wide timestamp recorded through Chronicler's versioned time-value schema;
cooldown durations remain Fixed64. Missing or retired jump timing records and
outer Navigator schemas, including version 8, reject transactionally. There is
no narrow-time fallback or inferred epoch. Hosts must restore controller timing
against its original simulation origin, not populate an active jump into an
unrelated zero-time world. Context clocks are not part of Navigator records.

Loading preserves active and buffered turns instead of resetting turning to
idle. The host must resume from the recorded authoritative rotation with the
same deterministic timestep. The turning controller's context, body-radius
threshold, and `CanTurnOnCollision` predicate remain shell-owned; the callback
is not serialized. Restoring a collision signal does not bypass that predicate.

For a Gravitas-backed Navigator, restore the authoritative body before the
Navigator. Successful population discards adapter-local prepared motion and its
cached contact; the next complete prepare/resolve/commit frame samples support
again. Rejected population preserves the prepared frame so it can still commit.
This is not a world rollback: hosts reconstruct world time, maps, policies,
platform transforms, and matching body/profile shells before resuming.

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
- current-foot connector proofs, raw epochs, or proof-byte reservations;
- the context logical clock, clock lifetime, or a phased due marker;
- `LastCommittedCell` or committed-cell notifications.

With a configured phase or Scheduled acquisition mode, restore discards
serialized direct-LOS trust and requires a fresh admission decision on the next
eligible steering update, whether loading into the same context or a new one. It
does not acquire a guide during population. With no phase, the established
direct/countdown restore behavior remains unchanged in Immediate mode. Navigator
population does not restore or advance the context clock.

## Transactional Load

Navigator loading stages and validates the top-level schema, exact profile,
position/frame condition, path session, steering, turning, motor, and locomotion
records before changing the existing live shell.

A successful runtime profile replacement becomes the Navigator's current
serialized profile. Population still requires a shell configured with that exact
profile; load does not call the runtime physical-replacement hook or resize a
host collider. Physical adapters such as `GravitasNavigator3D` must therefore
restore the matching body shape and accepted root before population. This keeps
the host body and navigation envelope consistent before the next fixed frame.

Malformed early or late nested data must preserve the previous shell, active
query, guide lease, pending instruction, acquisition ticket and mode, LOS
configuration, timing state, and active or buffered turn continuation. This
applies to JSON in both package families and to MemoryPack in the standard
package. A failed load does not partially stop the current session, replace
controller tuning, or release guidance. It also preserves the shell's existing
GridForge occupancy registration and committed-cell state.

Steering range factors use the same non-negative domain at runtime and on load.
Negative `GroupFactor` or `AvoidFactor` records reject with
`InvalidOperationException`; neither factor is applied before both validate.
Their wire names and canonical defaults (10 and 3) are unchanged, and zero is
valid. The full transactional guarantee above applies to Navigator population.

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
- turning settings, active target/completion, buffered target/interpolation, and
  collision auto-turn signal;
- motor and built-in locomotion state;
- heightmap-grounding settings;
- occupancy group identity.

Context-owned world data is separate.

## Maps And Overlays

`NavigationMap` bakes and runtime overlay events are host/world assets, not
embedded inside Navigator records. Persist them in the host's deterministic
world snapshot/event log:

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
Reference records use `RecordDeep`; non-nullable structs use `RecordDeepStruct`;
optional structs use `RecordNullableDeep`.

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

Scheduled guide requests, their FIFO position, continuations, validation turns,
adoption samples, connector ownership and unclaimed results are runtime-only.
Navigator records its durable acquisition mode and guided intent, but never this
transient ownership. Successful population clears prior session retry flavor and
proof/turn ownership; failed staged population preserves the live session
unchanged. Record host query intent if using the guide service directly; restore
maps/policies first, then resubmit that intent in deterministic order. Do not
serialize `NavigationGuideRequest` or assume a reset preserves pending work. See
[scheduled acquisition](Pathing.md#spreading-acquisition-across-fixed-frames).

## Related guides

- [Overview](Overview.md)
- [Map publication](MapPublication.md)
- [Pathing](Pathing.md)
- [Navigator](Navigator.md)
