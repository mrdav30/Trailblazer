using FixedMathSharp;
using System;

#if NET8_0_OR_GREATER
using System.Text.Json.Serialization;
#endif
#if !NET8_0_OR_GREATER
using System.Text.Json.Serialization.Shim;
#endif

namespace Trailblazer.Navigation;

/// <summary>
/// Represents a host-provided snapshot of a platform's stable identity and transform for the current frame.
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
    /// Whether this snapshot represents an active platform sample.
    /// </summary>
    public readonly bool Active => Id != 0;

    [JsonConstructor]
    public PlatformSnapshot(int id, Fixed4x4 transform)
    {
        Id = id;
        Transform = transform;
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
