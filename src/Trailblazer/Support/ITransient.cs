namespace Trailblazer.Support;

/// <summary>
/// Defines a property state that may change per frame and require synchronization.
/// </summary>
public interface ITransient
{
    /// <summary>
    /// Synchronizes transient properties from another instance.
    /// </summary>
    /// <param name="other">The other instance to sync with.</param>
    void SyncState(ITransient other);

    /// <summary>
    /// Clears transient properties, resetting them to default values.
    /// </summary>
    void ClearState();
}

