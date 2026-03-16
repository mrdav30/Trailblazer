namespace Trailblazer.Serialization;

/// <summary>
/// Helper for reading and writing nested exposable objects during an expose pass.
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
