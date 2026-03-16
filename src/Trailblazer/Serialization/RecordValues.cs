namespace Trailblazer.Serialization;

/// <summary>
/// Helper for reading and writing leaf values during a chronicler pass.
/// </summary>
public static class RecordValues
{
    /// <summary>
    /// Reads or writes a named value.
    /// </summary>
    public static void Look<T>(IChronicler chronicler, ref T value, string name, T defaultValue = default)
    {
        chronicler.LookValue(ref value, name, defaultValue);
    }
}
