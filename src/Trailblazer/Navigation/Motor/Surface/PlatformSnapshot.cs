//=======================================================================
// PlatformSnapshot.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Chronicler;
using FixedMathSharp;
using System;
using System.Text.Json.Serialization;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Represents a host-provided snapshot of a contacted surface's stable identity and transform for the current frame.
/// </summary>
public struct PlatformSnapshot : IEquatable<PlatformSnapshot>, IRecordable
{
    /// <summary>
    /// Stable identifier used to determine whether two surface snapshots refer to the same platform.
    /// </summary>
    private int _id;

    /// <inheritdoc cref="_id"/>
    public readonly int Id => _id;

    /// <summary>
    /// World-space transform sampled from the host for this frame.
    /// </summary>
    public Fixed4x4 Transform;

    /// <summary>
    /// Indicates that the sampled surface should not act as a kinematic carrier for platform locomotion.
    /// </summary>
    public bool Inert;

    /// <summary>
    /// Whether this snapshot represents an active platform sample.
    /// </summary>
    public readonly bool Active => Id != 0;

    /// <summary>
    /// Whether this sampled surface should participate in moving-platform attachment and motion transfer logic.
    /// </summary>
    public readonly bool SupportsKinematicMotion => Active && !Inert;

    /// <summary>
    /// Initializes a new instance of the PlatformSnapshot class with the specified identifier, transform, and inert state.
    /// </summary>
    /// <param name="id">The unique identifier for the platform snapshot.</param>
    /// <param name="transform">The transformation matrix representing the platform's position and orientation.</param>
    /// <param name="inert">true if the platform is inert and does not interact with other objects; otherwise, false.</param>
    [JsonConstructor]
    public PlatformSnapshot(int id, Fixed4x4 transform, bool inert = false)
    {
        _id = id;
        Transform = transform;
        Inert = inert;
    }

    /// <summary>
    /// Determines whether two PlatformSnapshot instances are equal.
    /// </summary>
    public static bool operator ==(PlatformSnapshot left, PlatformSnapshot right) => left.Equals(right);

    /// <summary>
    /// Determines whether two PlatformSnapshot instances are not equal.
    /// </summary>
    public static bool operator !=(PlatformSnapshot left, PlatformSnapshot right) => !(left == right);

    /// <summary>
    /// Two snapshots are considered the same platform when they share the same stable id.
    /// </summary>
    public readonly bool Equals(PlatformSnapshot other) => Id == other.Id;

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is PlatformSnapshot h && Equals(h);

    /// <inheritdoc/>
    public override readonly int GetHashCode() => Id;

    /// <inheritdoc/>
    public void RecordData(IChronicler chronicler)
    {
        chronicler.LookValue(ref _id, "Id", 0);
        chronicler.LookValue(ref Transform, "Transform", default);
        chronicler.LookValue(ref Inert, "Inert", false);
    }
}
