namespace Trailblazer;

/// <summary>
/// Defines stable ordering slots for internal <see cref="TrailblazerManager"/> lifecycle hooks.
/// </summary>
internal static class TrailblazerLifecycleOrder
{
    public const int PathingMaintenance = 100;

    public const int NavigationIdentityReset = 150;

    public const int NavigationReset = 200;
}
