using FixedMathSharp;
using System;
using System.Text.Json.Serialization;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Represents a host-provided snapshot of a contacted surface's stable identity and transform for the current frame.
/// </summary>
public struct PlatformSnapshot : IEquatable<PlatformSnapshot>
{
    /// <summary>
    /// Stable identifier used to determine whether two surface snapshots refer to the same platform.
    /// </summary>
    public readonly int Id;

    /// <summary>
    /// World-space transform sampled from the host for this frame.
    /// </summary>
    public Fixed4x4 Transform;

    /// <summary>
    /// Indicates that the sampled surface should not act as a kinematic carrier for platform locomotion.
    /// </summary>
    public readonly bool Inert;

    /// <summary>
    /// Whether this snapshot represents an active platform sample.
    /// </summary>
    public readonly bool Active => Id != 0;

    /// <summary>
    /// Whether this sampled surface should participate in moving-platform attachment and motion transfer logic.
    /// </summary>
    public readonly bool SupportsKinematicMotion => Active && !Inert;

    [JsonConstructor]
    public PlatformSnapshot(int id, Fixed4x4 transform, bool inert = false)
    {
        Id = id;
        Transform = transform;
        Inert = inert;
    }

    public static bool operator ==(PlatformSnapshot left, PlatformSnapshot right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PlatformSnapshot left, PlatformSnapshot right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Two snapshots are considered the same platform when they share the same stable id.
    /// </summary>
    public readonly bool Equals(PlatformSnapshot other) => Id == other.Id;
    public override readonly bool Equals(object obj) => obj is PlatformSnapshot h && Equals(h);
    public override readonly int GetHashCode() => Id;
}
