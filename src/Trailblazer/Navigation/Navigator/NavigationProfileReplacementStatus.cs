//=======================================================================
// NavigationProfileReplacementStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation;

/// <summary>Describes the result of an atomic runtime navigation-profile replacement.</summary>
public enum NavigationProfileReplacementStatus : byte
{
    /// <summary>The replacement profile and every dependent state value were committed.</summary>
    Applied,

    /// <summary>The replacement exactly matched the active profile, so no state was touched.</summary>
    Unchanged,

    /// <summary>The supplied profile was not initialized.</summary>
    InvalidProfile,

    /// <summary>The Navigator was inactive or had uncommitted fixed-frame motion.</summary>
    UnsafeLifecycle,

    /// <summary>A semantic navigation transition is awaiting host completion.</summary>
    ActionInProgress,

    /// <summary>The foot-preserving replacement root was outside the fixed-point coordinate domain.</summary>
    FootAnchorUnrepresentable,

    /// <summary>The replacement profile did not allow the host's current physical medium.</summary>
    CurrentMediumNotAllowed,

    /// <summary>The replacement profile did not fit the current navigation cell or occupancy state.</summary>
    NavigationClearanceRejected,

    /// <summary>The active destination intent could not be represented by the replacement profile.</summary>
    GuidanceIncompatible,

    /// <summary>The host rejected the corresponding physical body replacement.</summary>
    PhysicalFitRejected
}
