//=======================================================================
// NavigationMotionResult2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation;

/// <summary>Returns accepted physical motion and a fresh support sample through the host barrier.</summary>
public readonly struct NavigationMotionResult2D
{
    /// <summary>Creates an accepted result. Only Found carries a support snapshot.</summary>
    public NavigationMotionResult2D(NavigationPose2D accepted, NavigationSupportStatus2D supportStatus,
        NavigationSupport2D? support = null)
    {
        Accepted = accepted;
        SupportStatus = supportStatus;
        Support = support;
        Validate();
    }
    /// <summary>Gets the host-accepted pose.</summary>
    public NavigationPose2D Accepted { get; }

    /// <summary>Gets the freshness and applicability of the support sample.</summary>
    public NavigationSupportStatus2D SupportStatus { get; }

    /// <summary>Gets the physical support witness, only for Found.</summary>
    public NavigationSupport2D? Support { get; }

    internal void Validate()
    {
        ChronicleGuard.ThrowIfArgument(SupportStatus > NavigationSupportStatus2D.Unavailable
            || (SupportStatus == NavigationSupportStatus2D.Found) != Support.HasValue, nameof(SupportStatus));
        Support?.Validate();
    }
}
