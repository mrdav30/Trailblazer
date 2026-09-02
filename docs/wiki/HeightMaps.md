# Heightmaps

Trailblazer heightmaps are deterministic, context-owned ground/contact-height
lookups. They let a host ask "what is the environment Y at this X/Z contact
point?" without doing a raycast inside Trailblazer.

Heightmaps are engine-agnostic. Trailblazer consumes prebuilt height data, but
it does not bake terrain, meshes, colliders, or engine-specific scene data.
Baking belongs in a host or engine module that can produce compressed samples
for this core library.

## Model

A `HeightmapSurface` stores environment ground/contact Y over an X/Z lattice:

- X/Z bounds come from `MinBounds`, `Interval`, `Width`, and `Depth`.
- Y samples are compressed into `SwiftCollections.Dimensions.SwiftShortArray2D`.
- Runtime sampling always uses the compressed representation.
- Sampling uses deterministic fixed-point bilinear interpolation between
  neighboring samples.

The Y value is contact ground Y, not a navigator root/body Y. Navigator root
projection adds `BodyShape.RootToFootOffsetY` and any configured heightmap
`groundOffset` after sampling.

## Storage And Compression

`HeightmapCompression` defines how compact `short` samples convert to `Fixed64`.
Formula fragment:

```csharp
groundY = compression.ReferenceHeight + compressedSample * compression.HeightStep;
```

`ReferenceHeight` is the world Y represented by compressed value `0`.
`HeightStep` is the world Y delta represented by one compressed unit. Smaller
steps give finer precision but less usable height range before clamping to
`short.MinValue` or `short.MaxValue`. Larger steps give more range but coarser
samples.

Trailblazer exposes two factories:

- `HeightmapSurface.FromCompressed(...)` is the preferred baked/runtime path.
  Use it when the host already has compact samples from an authoring pipeline,
  serialized map data, or an engine-specific baker.
- `HeightmapSurface.FromHeights(...)` is a setup-time convenience. Use it for
  tests, generated maps, or tooling that already has `Fixed64[,]` heights and
  wants Trailblazer to perform the one-time quantization step.

Both factories produce the same runtime shape: a compressed `SwiftShortArray2D`
sampled through the same deterministic path.

## Registration

Heightmaps are registered on `TrailblazerWorldContext.Heightmaps`. This complete
C# setup example owns its context and sample storage:

```csharp
using FixedMathSharp;
using SwiftCollections.Dimensions;
using Trailblazer;
using Trailblazer.Heightmaps;

using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();

var samples = new SwiftShortArray2D(2, 2);
samples[0, 0] = 0;
samples[1, 0] = 2;
samples[0, 1] = 1;
samples[1, 1] = 3;

var compression = new HeightmapCompression(
    referenceHeight: Fixed64.Zero,
    heightStep: Fixed64.Half);

HeightmapSurface surface = HeightmapSurface.FromCompressed(
    name: "ArenaGround",
    samples: samples,
    minBounds: Vector3d.Zero,
    interval: Fixed64.One,
    compression: compression);

context.Heightmaps.Register(
    surface,
    minSelectionY: (Fixed64)(-1),
    maxSelectionY: (Fixed64)2);
```

Layer names are unique inside one context. The same layer name can be registered
independently in another `TrailblazerWorldContext`.

`TrailblazerHeightmapService.Reset()` clears only the current context's
heightmap registry. Disposing the context also clears its heightmaps.

## Sampling

Use `TrySampleGround(...)` with a world X/Z position and contact-selection Y.
C# fragment using the context created above:

```csharp
Vector3d contactQuery = new(0, 0, 0);

if (context.Heightmaps.TrySampleGround(contactQuery, out HeightmapSample sample))
{
    Fixed64 groundY = sample.GroundY;
    string layerName = sample.LayerName;
}
```

The no-preference overload considers every registered layer that contains the
query X/Z and whose vertical selection band contains the query Y. The
preferred-layer overload first tries the supplied layer name, then falls back to
deterministic candidate selection. C# fragment:

```csharp
context.Heightmaps.TrySampleGround(
    contactQuery,
    preferredLayerName: "ArenaGround",
    out HeightmapSample sample);
```

When several layers are valid, Trailblazer chooses:

1. the preferred/active layer when it is still valid
2. otherwise the layer whose sampled ground Y is nearest to the query contact Y
3. higher `priority`
4. earlier context-local registration order

## Multi-Level Layers

Multi-level worlds use one heightmap layer per selectable level. A platform
above a floor can share the same X/Z area with the floor because each
registration has a vertical selection band. C# fragment:

```csharp
context.Heightmaps.Register(groundSurface, minSelectionY: (Fixed64)(-1), maxSelectionY: (Fixed64)2);
context.Heightmaps.Register(platformSurface, minSelectionY: (Fixed64)2, maxSelectionY: (Fixed64)5);
```

Selection bands are inclusive at the lower bound and exclusive at the upper
bound. The heightmap does not care about GridForge voxel levels; the grid/world
only provides the context and world-space bounds that hosts use to decide which
layers to register.

For agents crossing between levels, keep passing the current active layer name
when possible. The service will keep that layer while it remains valid and
abandon it once the query contact Y leaves the layer's vertical band.

## Example: Unity-Style Editor Bake

One reasonable Unity workflow is to bake compact height samples in an editor
tool, then feed the result to Trailblazer at runtime. The Unity-specific part is
the scene query; the Trailblazer-facing result is just compressed samples plus
compression metadata.

A Unity-style editor baker should follow this shape:

1. Define a bake region, interval, height range, and layer mask.
2. Step across the region in X/Z sample coordinates.
3. Raycast downward from the top of the height range.
4. Store hit Y, or a fallback minimum Y when no hit exists.
5. Compress the Y value into `short` samples.
6. Save or emit the compact sample data for runtime registration.

In current Trailblazer terms, that workflow maps to `HeightmapCompression` and
`HeightmapSurface.FromCompressed(...)`. The next block is Unity editor-adapter
pseudocode; Unity types are intentionally not part of Trailblazer:

```csharp
using FixedMathSharp;
using SwiftCollections.Dimensions;
using Trailblazer.Heightmaps;

var compression = new HeightmapCompression(
    referenceHeight: minHeight,
    heightStep: Fixed64.One / 4);

var samples = new SwiftShortArray2D(width, depth);

for (int x = 0; x < width; x++)
for (int z = 0; z < depth; z++)
{
    Vector3 castOrigin = new(
        (float)(minBounds.x + x * interval),
        (float)maxHeight,
        (float)(minBounds.z + z * interval));

    Fixed64 groundY = minHeight;
    if (Physics.Raycast(
        castOrigin,
        Vector3.down,
        out RaycastHit hit,
        (float)(maxHeight - minHeight),
        groundMask,
        QueryTriggerInteraction.UseGlobal))
    {
        groundY = (Fixed64)hit.point.y;
    }

    samples[x, z] = compression.CompressClamped(groundY);
}

HeightmapSurface surface = HeightmapSurface.FromCompressed(
    name: "ArenaGround",
    samples: samples,
    minBounds: minBounds,
    interval: interval,
    compression: compression);

context.Heightmaps.Register(
    surface,
    minSelectionY: minHeight,
    maxSelectionY: maxHeight);
```

For multi-level environments, run the same kind of bake once per selectable
level or authored surface layer, then register each result with the vertical
selection band that should choose that level. A floor and an overhead platform
can share X/Z samples as long as their bands separate the contact-Y ranges.

Do not bake body/root offsets into the stored height samples. The heightmap
should represent the environment contact Y while each navigator decides how its
root sits above that contact.

## Navigator Grounding

`Navigator` does not perform hidden heightmap probes. Concrete navigators or
host adapters opt in by calling the protected `TryApplyHeightmapGrounding(...)`
helper from their own traversal probing code, usually inside
`CheckTrekCondition()` after deciding the navigator is grounded on solid
terrain. This C# fragment shows the host-owned subclass and later configuration;
the host still performs normal Navigator setup/initialization:

```csharp
using FixedMathSharp;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;

public sealed class HeightmapNavigator : Navigator
{
    public HeightmapNavigator(TrailblazerWorldContext context)
        : base(context)
    {
    }

    public override void CheckTrekCondition()
    {
        // Host collision/probing code should decide grounded state first.
        SetGroundContact(surfaceLevel: FrameSurfaceY);
        TryApplyHeightmapGrounding(
            updateMotorState: true,
            surfaceFriction: Fixed64.Half,
            motionTransfer: MotionTransfer.None);
    }

    private Fixed64 FrameSurfaceY => Fixed64.Zero;
}

navigator.ConfigureHeightmapGrounding(
    mode: HeightmapGroundingMode.SurfaceLevelAndPosition,
    layerName: "ArenaGround",
    groundOffset: Fixed64.Zero,
    snapTolerance: (Fixed64)2);
```

Modes:

- `HeightmapGroundingMode.Disabled` performs no sampling.
- `HeightmapGroundingMode.SurfaceLevelOnly` updates `SurfaceLevel`/ground
  contact but does not move the navigator root.
- `HeightmapGroundingMode.SurfaceLevelAndPosition` updates ground contact and
  projects root Y to
  `sample.GroundY + BodyShape.RootToFootOffsetY + groundOffset`.

Projection only runs while the current medium is `TraversalMedium.Solid`. This
keeps airborne, swimming, and other host-owned traversal phases from being
silently snapped by heightmaps. When projection does run, `Position` and
`LastPosition` shift together so the correction does not create fake vertical
velocity. If `snapTolerance` is set and the absolute correction exceeds it, the
ground contact can update while root projection is skipped.

The navigator caches `HeightmapGrounding.ActiveLayerName` after successful
grounding and uses it as the preferred layer on later calls. These settings are
serialized with the navigator, but heightmap surface data and context
registrations are not.

## Host Responsibilities

Hosts own:

- generating or baking heightmap samples
- choosing `HeightmapCompression` precision/range
- registering layers on the correct `TrailblazerWorldContext`
- choosing vertical selection bands for multi-level worlds
- deciding when a navigator is grounded before calling
  `TryApplyHeightmapGrounding(...)`
- re-registering heightmaps on the context before loaded navigators use restored
  grounding settings

Trailblazer owns deterministic storage, sampling, context-local registration,
layer selection, and the navigator opt-in helper.
