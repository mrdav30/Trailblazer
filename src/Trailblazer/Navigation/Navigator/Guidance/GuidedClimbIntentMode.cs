//=======================================================================
// GuidedClimbIntentMode.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation;

/// <summary>
/// Distinguishes host-owned guided climb intent from route-derived guided climb intent.
/// </summary>
internal enum GuidedClimbIntentMode
{
    Auto = 0,
    Explicit = 1
}
