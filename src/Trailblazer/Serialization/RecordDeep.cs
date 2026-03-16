namespace Trailblazer.Serialization;

/// <summary>
/// Helper for reading and writing nested recordable objects during a chronicler pass.
/// </summary>
public static class RecordDeep
{
    /// <summary>
    /// Reads or writes a named nested object.
    /// </summary>
    public static void Look<T>(IChronicler chronicler, ref T value, string name) where T : class, IRecordable
    {
        chronicler.LookDeep(ref value, name);
    }
}
