using Chronicler;

namespace Trailblazer.Tests;

/// <summary>
/// Utility methods for testing Trailblazer serialization.
/// </summary>
public static class SerializationUtility
{

    public static object SerializeRecord(IRecordable record, bool useMemoryPack)
    {
        return useMemoryPack
            ? MemoryPackRecordSerializer.Serialize(record)
            : JsonRecordSerializer.Serialize(record, writeIndented: true);
    }

    public static void PopulateRecord(IRecordable target, object payload, bool useMemoryPack)
    {
        if (useMemoryPack)
        {
            MemoryPackRecordSerializer.Populate(target, (byte[])payload);
            return;
        }

        JsonRecordSerializer.Populate(target, (string)payload);
    }

    public static object RemovePayloadEntry(object payload, bool useMemoryPack, params string[] path)
    {
        return useMemoryPack
            ? SerializationPayloadEditor.RemoveMemoryPackEntry((byte[])payload, path)
            : SerializationPayloadEditor.RemoveJsonProperty((string)payload, path);
    }

    public static object SetPayloadValue<T>(object payload, bool useMemoryPack, T value, params string[] path)
    {
        return useMemoryPack
            ? SerializationPayloadEditor.SetMemoryPackValue((byte[])payload, value, path)
            : SerializationPayloadEditor.SetJsonValue((string)payload, value, path);
    }
}