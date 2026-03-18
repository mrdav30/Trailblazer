using System;

namespace Trailblazer.Serialization;

/// <summary>
/// Defines the common contract used by value and deep serialization helpers.
/// </summary>
public interface IChronicler
{
    /// <summary>
    /// Gets the session context for the current chronicler pass.
    /// </summary>
    ChronicleContext Context { get; }

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

    /// <summary>
    /// Reads or writes a nested recordable struct by name.
    /// </summary>
    void LookDeepStruct<T>(ref T value, string name) where T : struct, IRecordable;

    /// <summary>
    /// Reads or writes an optional nested recordable struct by name.
    /// </summary>
    void LookNullableDeep<T>(ref T? value, string name) where T : struct, IRecordable;

    /// <summary>
    /// Reads or writes a stable link to an external or runtime-owned value by name.
    /// </summary>
    void LookLink<T>(
        ref T value,
        string name,
        string slot = null,
        RecordLinkResolveMode resolveMode = RecordLinkResolveMode.Immediate,
        Action<T> assignLoadedValue = null);
}
