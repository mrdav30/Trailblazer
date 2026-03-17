using System;

namespace Trailblazer.Serialization;

/// <summary>
/// Helper for reading and writing stable links to external or runtime-owned objects during a chronicler pass.
/// </summary>
public static class RecordLinks
{
    /// <summary>
    /// Reads or writes a named external link that must resolve during the current load pass.
    /// </summary>
    public static void Look<T>(IChronicler chronicler, ref T value, string name, string slot = null)
    {
        chronicler.LookLink(ref value, name, slot);
    }

    /// <summary>
    /// Reads or writes a named external link that may resolve after the current load graph finishes.
    /// </summary>
    public static void LookDeferred<T>(
        IChronicler chronicler,
        T value,
        string name,
        Action<T> assignLoadedValue,
        string slot = null)
    {
        if (assignLoadedValue == null)
            throw new ArgumentNullException(nameof(assignLoadedValue));

        chronicler.LookLink(
            ref value,
            name,
            slot,
            RecordLinkResolveMode.Deferred,
            assignLoadedValue);
    }
}
