using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes one endpoint of an authored traversal transition.
/// </summary>
[Serializable]
public readonly struct TraversalTransitionAnchor
{
    /// <summary>
    /// Identifies whether this anchor belongs to chart-backed traversal or raw volume traversal.
    /// </summary>
    public TraversalTransitionAnchorKind Kind { get; }

    /// <summary>
    /// The world position where this anchor is authored.
    /// </summary>
    public Vector3d Position { get; }

    /// <summary>
    /// The raw volume mode used when <see cref="Kind"/> is <see cref="TraversalTransitionAnchorKind.Volume"/>.
    /// </summary>
    public VolumeTraversalMode VolumeMode { get; }

    /// <summary>
    /// Creates a chart-backed anchor at the provided world position.
    /// </summary>
    public static TraversalTransitionAnchor Chart(Vector3d position) =>
        new(TraversalTransitionAnchorKind.Chart, position, VolumeTraversalMode.Open);

    /// <summary>
    /// Creates a raw-volume anchor at the provided world position.
    /// </summary>
    public static TraversalTransitionAnchor Volume(Vector3d position, VolumeTraversalMode volumeMode) =>
        new(TraversalTransitionAnchorKind.Volume, position, volumeMode);

    public TraversalTransitionAnchor(
        TraversalTransitionAnchorKind kind,
        Vector3d position,
        VolumeTraversalMode volumeMode = VolumeTraversalMode.Open)
    {
        Kind = kind;
        Position = position;
        VolumeMode = volumeMode;
    }
}
