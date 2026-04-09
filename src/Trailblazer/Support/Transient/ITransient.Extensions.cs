namespace Trailblazer.Support;

/// <summary>
/// Extension methods for ITransient to allow calling without casting to the interface.
/// This is purely for convenience and does not change the underlying behavior.
/// </summary>
public static class ITransientExtensions
{
    /// <inheritdoc cref="ITransient.SyncTransientState"/>
    public static void SyncTransientState(this ITransient instance, ITransient other)
        => instance.SyncTransientState(other);

    /// <inheritdoc cref="ITransient.ClearTransientState"/>
    public static void ClearTransientState(this ITransient instance)
        => instance.ClearTransientState();
}
