namespace Trailblazer.Serialization;

/// <summary>
/// Helper for reading and writing nested recordable structs during a chronicler pass.
/// </summary>
public static class RecordDeepStruct
{
    /// <summary>
    /// Reads or writes a named nested struct.
    /// </summary>
    public static void Look<T>(IChronicler chronicler, ref T value, string name) where T : struct, IRecordable
    {
        chronicler.LookDeepStruct(ref value, name);
    }
}
