using Chronicler;
using System;

namespace Trailblazer.Tests;

/// <summary>
/// Utility methods for testing Trailblazer serialization.
/// </summary>
public static class SerializationUtility
{

    public static object SerializeRecord(IRecordable record, bool useMemoryPack)
    {
#if !TRAILBLAZER_DISABLE_MEMORYPACK
        return useMemoryPack
            ? MemoryPackRecordSerializer.Serialize(record)
            : JsonRecordSerializer.Serialize(record, writeIndented: true);
#else
        if (useMemoryPack)
            throw new NotSupportedException("MemoryPack serialization is not available in the lean test configuration.");

        return JsonRecordSerializer.Serialize(record, writeIndented: true);
#endif
    }

    public static void PopulateRecord(IRecordable target, object payload, bool useMemoryPack)
    {
#if !TRAILBLAZER_DISABLE_MEMORYPACK
        if (useMemoryPack)
        {
            MemoryPackRecordSerializer.Populate(target, (byte[])payload);
            return;
        }
#else
        if (useMemoryPack)
            throw new NotSupportedException("MemoryPack serialization is not available in the lean test configuration.");
#endif

        JsonRecordSerializer.Populate(target, (string)payload);
    }

    public static object RemovePayloadEntry(object payload, bool useMemoryPack, params string[] path)
    {
#if !TRAILBLAZER_DISABLE_MEMORYPACK
        return useMemoryPack
            ? SerializationPayloadEditor.RemoveMemoryPackEntry((byte[])payload, path)
            : SerializationPayloadEditor.RemoveJsonProperty((string)payload, path);
#else
        if (useMemoryPack)
            throw new NotSupportedException("MemoryPack serialization is not available in the lean test configuration.");

        return SerializationPayloadEditor.RemoveJsonProperty((string)payload, path);
#endif
    }

    public static object SetPayloadValue<T>(object payload, bool useMemoryPack, T value, params string[] path)
    {
#if !TRAILBLAZER_DISABLE_MEMORYPACK
        return useMemoryPack
            ? SerializationPayloadEditor.SetMemoryPackValue((byte[])payload, value, path)
            : SerializationPayloadEditor.SetJsonValue((string)payload, value, path);
#else
        if (useMemoryPack)
            throw new NotSupportedException("MemoryPack serialization is not available in the lean test configuration.");

        return SerializationPayloadEditor.SetJsonValue((string)payload, value, path);
#endif
    }
}
