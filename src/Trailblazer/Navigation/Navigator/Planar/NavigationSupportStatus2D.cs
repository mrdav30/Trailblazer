//=======================================================================
// NavigationSupportStatus2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation;

/// <summary>Distinguishes a physical support witness, a complete miss, and an unavailable sample.</summary>
public enum NavigationSupportStatus2D : byte
{
    /// <summary>Support is inapplicable to top-down motion.</summary>
    NotApplicable,
    /// <summary>The host completed the sample without a support witness.</summary>
    NoSupport,
    /// <summary>The host supplied a support witness at the accepted pose.</summary>
    Found,
    /// <summary>The host could not provide a fresh sample.</summary>
    Unavailable
}
