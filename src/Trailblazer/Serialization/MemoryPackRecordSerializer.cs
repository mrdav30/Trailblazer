using System;
using System.Collections.Generic;
using MemoryPack;

namespace Trailblazer.Serialization;

/// <summary>
/// Serializes <see cref="IRecordable"/> state graphs to and from MemoryPack through the chronicler API.
/// </summary>
public static class MemoryPackRecordSerializer
{
    /// <summary>
    /// Serializes the current state of a recordable instance into MemoryPack bytes.
    /// </summary>
    public static byte[] Serialize(IRecordable target)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        var chronicler = new MemoryPackRecordWriter();
        target.RecordData(chronicler);
        return chronicler.ToArray();
    }

    /// <summary>
    /// Loads MemoryPack state into an existing recordable instance.
    /// </summary>
    public static void Populate(IRecordable target, byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        Populate(target, data.AsSpan());
    }

    /// <summary>
    /// Loads MemoryPack state into an existing recordable instance.
    /// </summary>
    public static void Populate(IRecordable target, ReadOnlySpan<byte> data)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (data.IsEmpty)
            throw new ArgumentException("Serialized bytes must not be empty.", nameof(data));

        var chronicler = new MemoryPackRecordReader(data);
        target.RecordData(chronicler);
    }

    private sealed class MemoryPackRecordWriter : IChronicler
    {
        private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

        public SerializationMode Mode => SerializationMode.Saving;

        public void LookValue<T>(ref T value, string name, T defaultValue = default)
        {
            _entries[name] = MemoryPackSerializer.Serialize(value);
        }

        public void LookDeep<T>(ref T value, string name) where T : class, IRecordable
        {
            if (value == null)
            {
                _entries[name] = null;
                return;
            }

            var nested = new MemoryPackRecordWriter();
            value.RecordData(nested);
            _entries[name] = nested.ToArray();
        }

        public byte[] ToArray()
        {
            return MemoryPackSerializer.Serialize(new MemoryPackRecordEnvelope()
            {
                Entries = _entries
            });
        }
    }

    private sealed class MemoryPackRecordReader : IChronicler
    {
        private readonly Dictionary<string, byte[]> _entries;

        public MemoryPackRecordReader(ReadOnlySpan<byte> data)
        {
            MemoryPackRecordEnvelope envelope = MemoryPackSerializer.Deserialize<MemoryPackRecordEnvelope>(data);
            _entries = envelope?.Entries ?? new Dictionary<string, byte[]>(StringComparer.Ordinal);
        }

        public SerializationMode Mode => SerializationMode.Loading;

        public void LookValue<T>(ref T value, string name, T defaultValue = default)
        {
            if (!_entries.TryGetValue(name, out byte[] entry)
                || entry == null)
            {
                value = defaultValue;
                return;
            }

            T loadedValue = MemoryPackSerializer.Deserialize<T>(entry);
            value = loadedValue == null ? defaultValue : loadedValue;
        }

        public void LookDeep<T>(ref T value, string name) where T : class, IRecordable
        {
            if (!_entries.TryGetValue(name, out byte[] entry)
                || entry == null)
                return;

            if (value == null)
                throw new InvalidOperationException(
                    $"Unable to load '{name}' because {typeof(T).Name} must already be instantiated for a deep chronicler load.");

            var nested = new MemoryPackRecordReader(entry);
            value.RecordData(nested);
        }
    }
}
