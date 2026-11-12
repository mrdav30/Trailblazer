//=======================================================================
// NavigationAgentProfile2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Defines the native planar body and navigation permissions, independently of motor tuning.</summary>
public readonly struct NavigationAgentProfile2D : IEquatable<NavigationAgentProfile2D>
{
    /// <summary>Creates an upright capsule profile, or a top-down circle with zero support fields.</summary>
    public NavigationAgentProfile2D(NavigationMode2D mode, Fixed64 radius, Fixed64 axisLength,
        Fixed64 maxStepUp, Fixed64 maxDropDown, Fixed64 minimumSupportNormalDot,
        Fixed64 arrivalRadius, TraversalMedia allowedMedia, TraversalCapability capabilities)
    {
        Mode = mode;
        Radius = radius;
        AxisLength = axisLength;
        MaxStepUp = maxStepUp;
        MaxDropDown = maxDropDown;
        MinimumSupportNormalDot = minimumSupportNormalDot;
        ArrivalRadius = arrivalRadius;
        AllowedMedia = allowedMedia;
        Capabilities = capabilities;
        Validate(nameof(radius));
    }

    /// <summary>Gets the native motion mode.</summary>
    public NavigationMode2D Mode { get; }

    /// <summary>Gets the positive cap radius.</summary>
    public Fixed64 Radius { get; }

    /// <summary>Gets the non-negative separation between cap centers.</summary>
    public Fixed64 AxisLength { get; }

    internal Fixed64 HalfAxisCeiling => Fixed64.FromRaw((AxisLength.m_rawValue >> 1) + (AxisLength.m_rawValue & 1));

    internal Fixed64 NominalFootOffset => Radius + Fixed64.FromRaw(AxisLength.m_rawValue >> 1);

    /// <summary>Gets the largest admitted upward support step.</summary>
    public Fixed64 MaxStepUp { get; }

    /// <summary>Gets the largest ordinary downward support drop.</summary>
    public Fixed64 MaxDropDown { get; }

    /// <summary>Gets the minimum unit support-normal component along planar Forward.</summary>
    public Fixed64 MinimumSupportNormalDot { get; }

    /// <summary>Gets the non-negative arrival tolerance.</summary>
    public Fixed64 ArrivalRadius { get; }

    /// <summary>Gets the permitted traversal media.</summary>
    public TraversalMedia AllowedMedia { get; }

    /// <summary>Gets the admitted traversal capabilities.</summary>
    public TraversalCapability Capabilities { get; }

    internal void Validate(string parameterName)
    {
        ChronicleGuard.ThrowIfArgument(Mode is not NavigationMode2D.TopDown and not NavigationMode2D.SideView
            || Radius <= Fixed64.Zero || AxisLength < Fixed64.Zero
            || Radius.m_rawValue > (long.MaxValue - AxisLength.m_rawValue) / 2
            || MaxStepUp < Fixed64.Zero || MaxDropDown < Fixed64.Zero || ArrivalRadius < Fixed64.Zero
            || AllowedMedia == TraversalMedia.None || (AllowedMedia & ~NavigationCell.KnownMedia) != 0
            || (Capabilities & ~NavigationCell.KnownCapabilities) != 0, parameterName);
        ChronicleGuard.ThrowIfArgument(Mode == NavigationMode2D.TopDown
            ? AxisLength != Fixed64.Zero || MaxStepUp != Fixed64.Zero || MaxDropDown != Fixed64.Zero
                || MinimumSupportNormalDot != Fixed64.Zero
            : MinimumSupportNormalDot <= Fixed64.Zero || MinimumSupportNormalDot > Fixed64.One, parameterName);
    }

    /// <inheritdoc/>
    public bool Equals(NavigationAgentProfile2D other) => Mode == other.Mode && Radius == other.Radius
        && AxisLength == other.AxisLength && MaxStepUp == other.MaxStepUp && MaxDropDown == other.MaxDropDown
        && MinimumSupportNormalDot == other.MinimumSupportNormalDot && ArrivalRadius == other.ArrivalRadius
        && AllowedMedia == other.AllowedMedia && Capabilities == other.Capabilities;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NavigationAgentProfile2D other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes((int)Mode, Radius.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, AxisLength.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, MaxStepUp.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, MaxDropDown.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, MinimumSupportNormalDot.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, ArrivalRadius.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, (int)AllowedMedia);
        return SwiftHashTools.CombineHashCodes(hash, (int)Capabilities);
    }

    /// <summary>Compares every geometry and permission field.</summary>
    public static bool operator ==(NavigationAgentProfile2D left, NavigationAgentProfile2D right) => left.Equals(right);

    /// <summary>Compares every geometry and permission field.</summary>
    public static bool operator !=(NavigationAgentProfile2D left, NavigationAgentProfile2D right) => !left.Equals(right);
}
