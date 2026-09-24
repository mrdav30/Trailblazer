//=======================================================================
// NavigationValidationInterest.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Intrusive eligibility and optional actual connector ownership; no query or continuation.</summary>
internal sealed class NavigationValidationInterest
{
    internal NavigationGuideScheduler? Owner;
    internal NavigationValidationInterest? Next;
    internal NavigationValidationInterest? Previous;
    internal object? GrantedLifetime;
    internal long GrantedFrame;
    // Generation-checked alias, not another payload lease/pin. Reset releases this actual owner.
    internal NavigationGuideLease ConnectorGuide;
}
