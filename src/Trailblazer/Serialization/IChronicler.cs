namespace Trailblazer.Serialization;

/// <summary>
/// Defines the common contract used by value and deep serialization helpers.
/// </summary>
public interface IChronicler
{
    /// <summary>
    /// Gets the active serialization mode.
    /// </summary>
    SerializationMode Mode { get; }

    /// <summary>
    /// Reads or writes a value by name.
    /// </summary>
    void LookValue<T>(ref T value, string name, T defaultValue = default);

    /// <summary>
    /// Reads or writes a nested recordable instance by name.
    /// </summary>
    void LookDeep<T>(ref T value, string name) where T : class, IRecordable;
}
