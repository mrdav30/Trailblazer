//=======================================================================
// NavigationAgentProfile.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines immutable geometry and traversal capabilities for a navigation query.
/// </summary>
public readonly struct NavigationAgentProfile : IEquatable<NavigationAgentProfile>
{
    private const TraversalCapability KnownCapabilities =
        TraversalCapability.Jump
        | TraversalCapability.Climb
        | TraversalCapability.Swim
        | TraversalCapability.Fly
        | TraversalCapability.Teleport;

    /// <summary>
    /// Gets the authoritative body geometry for the agent.
    /// </summary>
    public KinematicBodyShape Shape { get; }

    /// <summary>
    /// Gets the maximum upward surface step allowed in world units.
    /// </summary>
    public Fixed64 MaxStepUp { get; }

    /// <summary>
    /// Gets the maximum downward surface drop allowed in world units.
    /// </summary>
    public Fixed64 MaxDropDown { get; }

    /// <summary>
    /// Gets the non-negative distance at which the agent is considered to have arrived.
    /// </summary>
    public Fixed64 ArrivalRadius { get; }

    /// <summary>
    /// Gets the traversal media this agent may enter.
    /// </summary>
    public TraversalMedia AllowedMedia { get; }

    /// <summary>
    /// Gets the additional traversal abilities available to the agent.
    /// </summary>
    public TraversalCapability Capabilities { get; }

    /// <summary>
    /// Creates an immutable navigation agent profile.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the body shape is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a distance is negative or flags contain unknown bits.</exception>
    public NavigationAgentProfile(
        KinematicBodyShape shape,
        Fixed64 maxStepUp,
        Fixed64 maxDropDown,
        Fixed64 arrivalRadius,
        TraversalMedia allowedMedia,
        TraversalCapability capabilities)
    {
        shape.Validate(nameof(shape));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxStepUp < Fixed64.Zero,
            actualValue: null,
            nameof(maxStepUp),
            "Maximum step-up distance cannot be negative.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxDropDown < Fixed64.Zero,
            actualValue: null,
            nameof(maxDropDown),
            "Maximum drop-down distance cannot be negative.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            arrivalRadius < Fixed64.Zero,
            actualValue: null,
            nameof(arrivalRadius),
            "Arrival radius cannot be negative.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            (allowedMedia & ~NavigationCell.KnownMedia) != 0,
            (int)allowedMedia,
            nameof(allowedMedia),
            "Allowed media contains unknown flag bits.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            (capabilities & ~KnownCapabilities) != 0,
            (int)capabilities,
            nameof(capabilities),
            "Traversal capabilities contain unknown flag bits.");

        Shape = shape;
        MaxStepUp = maxStepUp;
        MaxDropDown = maxDropDown;
        ArrivalRadius = arrivalRadius;
        AllowedMedia = allowedMedia;
        Capabilities = capabilities;
    }

    /// <inheritdoc/>
    public bool Equals(NavigationAgentProfile other) =>
        Shape == other.Shape
        && MaxStepUp == other.MaxStepUp
        && MaxDropDown == other.MaxDropDown
        && ArrivalRadius == other.ArrivalRadius
        && AllowedMedia == other.AllowedMedia
        && Capabilities == other.Capabilities;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NavigationAgentProfile other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(Shape.GetHashCode(), MaxStepUp.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, MaxDropDown.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, ArrivalRadius.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, (int)AllowedMedia);
        return SwiftHashTools.CombineHashCodes(hash, (int)Capabilities);
    }

    /// <summary>
    /// Returns whether two profiles have exactly equal values.
    /// </summary>
    public static bool operator ==(NavigationAgentProfile left, NavigationAgentProfile right) => left.Equals(right);

    /// <summary>
    /// Returns whether two profiles differ.
    /// </summary>
    public static bool operator !=(NavigationAgentProfile left, NavigationAgentProfile right) => !left.Equals(right);

    internal bool IsValid => Shape.IsValid;

    internal void Validate(string parameterName)
    {
        Shape.Validate(parameterName);
    }
}
