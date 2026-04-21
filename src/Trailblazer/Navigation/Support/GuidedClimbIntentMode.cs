namespace Trailblazer.Navigation;

/// <summary>
/// Distinguishes host-owned guided climb intent from route-derived guided climb intent.
/// </summary>
internal enum GuidedClimbIntentMode
{
    Auto = 0,
    Explicit = 1
}
