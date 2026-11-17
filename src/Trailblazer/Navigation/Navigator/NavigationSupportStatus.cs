//=======================================================================
// NavigationSupportStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation;

/// <summary>Distinguishes a physical support witness, a complete miss, and an unavailable sample.</summary>
public enum NavigationSupportStatus : byte
{
    /// <summary>Support is inapplicable or no sample has been taken.</summary>
    NotApplicable,
    /// <summary>The sample found no eligible support. Includes empty results from synchronous sampling.</summary>
    NoSupport,
    /// <summary>The host supplied a support witness at the accepted pose.</summary>
    Found,
    /// <summary>The host could not provide a fresh sample.</summary>
    Unavailable
}
