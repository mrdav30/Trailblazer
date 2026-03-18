namespace Trailblazer.Serialization;

/// <summary>
/// Helper for reading and writing optional nested recordable structs during a chronicler pass.
/// </summary>
public static class RecordNullableDeep
{
    /// <summary>
    /// Reads or writes a named optional nested struct.
    /// </summary>
    public static void Look<T>(IChronicler chronicler, ref T? value, string name) where T : struct, IRecordable
    {
        chronicler.LookNullableDeep(ref value, name);
    }
}
