using Chronicler;
using FixedMathSharp;
using System;

namespace Trailblazer.Navigation;

/// <summary>
/// Navigator-owned opt-in settings for consuming context-registered heightmap layers.
/// </summary>
public sealed class NavigatorHeightmapGroundingSettings : IRecordable
{
    /// <summary>
    /// Gets the configured heightmap grounding mode.
    /// </summary>
    public HeightmapGroundingMode Mode { get; private set; }

    /// <summary>
    /// Gets the optional configured layer preference used before an active layer has been established.
    /// </summary>
    public string? LayerName { get; private set; }

    /// <summary>
    /// Gets the active layer selected by the most recent successful heightmap grounding sample.
    /// </summary>
    public string? ActiveLayerName { get; internal set; }

    /// <summary>
    /// Gets the extra root offset applied above sampled ground Y.
    /// </summary>
    public Fixed64 GroundOffset { get; private set; }

    /// <summary>
    /// Gets the maximum allowed root-Y correction for positional projection, or null for no limit.
    /// </summary>
    public Fixed64? SnapTolerance { get; private set; }

    internal void Configure(
        HeightmapGroundingMode mode,
        string? layerName,
        Fixed64? groundOffset,
        Fixed64? snapTolerance)
    {
        ValidateMode(mode);
        if (snapTolerance.HasValue && snapTolerance.Value < Fixed64.Zero)
            throw new ArgumentOutOfRangeException(nameof(snapTolerance), "Heightmap snap tolerance cannot be negative.");

        Mode = mode;
        LayerName = string.IsNullOrWhiteSpace(layerName) ? null : layerName;
        GroundOffset = groundOffset ?? Fixed64.Zero;
        SnapTolerance = snapTolerance;
        ActiveLayerName = mode == HeightmapGroundingMode.Disabled ? null : LayerName;
    }

    internal void Reset()
    {
        Mode = HeightmapGroundingMode.Disabled;
        LayerName = null;
        ActiveLayerName = null;
        GroundOffset = Fixed64.Zero;
        SnapTolerance = null;
    }

    /// <inheritdoc/>
    public void RecordData(IChronicler chronicler)
    {
        HeightmapGroundingMode mode = Mode;
        string? layerName = LayerName;
        string? activeLayerName = ActiveLayerName;
        Fixed64 groundOffset = GroundOffset;
        bool hasSnapTolerance = SnapTolerance.HasValue;
        Fixed64 snapTolerance = SnapTolerance ?? Fixed64.Zero;

        RecordValues.Look(chronicler, ref mode, "Mode", HeightmapGroundingMode.Disabled);
        RecordValues.Look(chronicler, ref layerName, "LayerName", null);
        RecordValues.Look(chronicler, ref activeLayerName, "ActiveLayerName", null);
        RecordValues.Look(chronicler, ref groundOffset, "GroundOffset", Fixed64.Zero);
        RecordValues.Look(chronicler, ref hasSnapTolerance, "HasSnapTolerance", false);
        RecordValues.Look(chronicler, ref snapTolerance, "SnapTolerance", Fixed64.Zero);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            ValidateMode(mode);
            if (hasSnapTolerance && snapTolerance < Fixed64.Zero)
                throw new ArgumentOutOfRangeException(nameof(SnapTolerance), "Heightmap snap tolerance cannot be negative.");

            Mode = mode;
            LayerName = string.IsNullOrWhiteSpace(layerName) ? null : layerName;
            ActiveLayerName = string.IsNullOrWhiteSpace(activeLayerName) ? null : activeLayerName;
            GroundOffset = groundOffset;
            SnapTolerance = hasSnapTolerance ? snapTolerance : null;
        }
    }

    private static void ValidateMode(HeightmapGroundingMode mode)
    {
        switch (mode)
        {
            case HeightmapGroundingMode.Disabled:
            case HeightmapGroundingMode.SurfaceLevelOnly:
            case HeightmapGroundingMode.SurfaceLevelAndPosition:
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), "Unknown heightmap grounding mode.");
        }
    }
}
