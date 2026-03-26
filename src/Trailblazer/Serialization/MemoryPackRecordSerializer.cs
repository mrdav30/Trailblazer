using MemoryPack;
using SwiftCollections;
using System;

namespace Trailblazer.Serialization;

/// <summary>
/// Serializes <see cref="IRecordable"/> state graphs to and from MemoryPack through the chronicler API.
/// </summary>
public static class MemoryPackRecordSerializer
{
    private static readonly byte[] EmptyRecordBytes = MemoryPackSerializer.Serialize(new MemoryPackRecordEnvelope());

    /// <summary>
    /// Serializes the current state of a recordable instance into MemoryPack bytes.
    /// </summary>
    public static byte[] Serialize(IRecordable target)
        => Serialize(target, context: null);

    /// <summary>
    /// Serializes the current state of a recordable instance into MemoryPack bytes.
    /// </summary>
    public static byte[] Serialize(IRecordable target, ChronicleContext context)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        context ??= new ChronicleContext();

        var chronicler = new MemoryPackRecordWriter(context);
        target.RecordData(chronicler);
        return chronicler.ToArray();
    }

    /// <summary>
    /// Loads MemoryPack state into an existing recordable instance.
    /// </summary>
    public static void Populate(IRecordable target, byte[] data)
        => Populate(target, data, context: null);

    /// <summary>
    /// Loads MemoryPack state into an existing recordable instance.
    /// </summary>
    public static void Populate(IRecordable target, byte[] data, ChronicleContext context)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        Populate(target, data.AsSpan(), context);
    }

    /// <summary>
    /// Loads MemoryPack state into an existing recordable instance.
    /// </summary>
    public static void Populate(IRecordable target, ReadOnlySpan<byte> data)
        => Populate(target, data, context: null);

    /// <summary>
    /// Loads MemoryPack state into an existing recordable instance.
    /// </summary>
    public static void Populate(IRecordable target, ReadOnlySpan<byte> data, ChronicleContext context)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (data.IsEmpty)
            throw new ArgumentException("Serialized bytes must not be empty.", nameof(data));

        context ??= new ChronicleContext();

        var chronicler = new MemoryPackRecordReader(data, context);
        target.RecordData(chronicler);
        context.ResolveDeferredLinks();
    }

    private sealed class MemoryPackRecordWriter : IChronicler
    {
        private readonly SwiftDictionary<string, byte[]> _entries = new(8, StringComparer.Ordinal);

        public MemoryPackRecordWriter(ChronicleContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public ChronicleContext Context { get; }

        public SerializationMode Mode => SerializationMode.Saving;

        public void LookValue<T>(ref T value, string name, T defaultValue = default)
        {
            if (value == null || value.Equals(defaultValue))
                return;
            _entries[name] = MemoryPackSerializer.Serialize(value);
        }

        public void LookDeep<T>(ref T value, string name) where T : class, IRecordable
        {
            if (value == null)
            {
                _entries[name] = null;
                return;
            }

            var nested = new MemoryPackRecordWriter(Context);
            value.RecordData(nested);
            _entries[name] = nested.ToArray();
        }

        public void LookDeepStruct<T>(ref T value, string name) where T : struct, IRecordable
        {
            var nested = new MemoryPackRecordWriter(Context);
            value.RecordData(nested);
            _entries[name] = nested.ToArray();
        }

        public void LookNullableDeep<T>(ref T? value, string name) where T : struct, IRecordable
        {
            if (!value.HasValue)
                return;

            T nestedValue = value.Value;
            var nested = new MemoryPackRecordWriter(Context);
            nestedValue.RecordData(nested);
            _entries[name] = nested.ToArray();
        }

        public void LookLink<T>(
            ref T value,
            string name,
            string slot = null,
            RecordLinkResolveMode resolveMode = RecordLinkResolveMode.Immediate,
            Action<T> assignLoadedValue = null)
        {
            string id = null;
            if (value is not null
                && !Context.Links.TryGetReferenceId(value, out id, slot))
            {
                throw new InvalidOperationException(
                    $"Unable to save link '{name}' of type {typeof(T).Name} because no stable id could be produced{FormatSlot(slot)}.");
            }

            _entries[name] = MemoryPackSerializer.Serialize(id);
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
        private readonly SwiftDictionary<string, byte[]> _entries;

        public MemoryPackRecordReader(ReadOnlySpan<byte> data, ChronicleContext context)
        {
            MemoryPackRecordEnvelope envelope = MemoryPackSerializer.Deserialize<MemoryPackRecordEnvelope>(data);
            _entries = envelope?.Entries ?? new SwiftDictionary<string, byte[]>(8, StringComparer.Ordinal);
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public ChronicleContext Context { get; }

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

            var nested = new MemoryPackRecordReader(entry, Context);
            value.RecordData(nested);
        }

        public void LookDeepStruct<T>(ref T value, string name) where T : struct, IRecordable
        {
            value = CreateDefaultDeepStruct<T>();

            if (!_entries.TryGetValue(name, out byte[] entry)
                || entry == null)
                return;

            var nested = new MemoryPackRecordReader(entry, Context);
            value.RecordData(nested);
        }

        public void LookNullableDeep<T>(ref T? value, string name) where T : struct, IRecordable
        {
            if (!_entries.TryGetValue(name, out byte[] entry)
                || entry == null)
            {
                value = null;
                return;
            }

            T nestedValue = CreateDefaultDeepStruct<T>();
            var nested = new MemoryPackRecordReader(entry, Context);
            nestedValue.RecordData(nested);
            value = nestedValue;
        }

        public void LookLink<T>(
            ref T value,
            string name,
            string slot = null,
            RecordLinkResolveMode resolveMode = RecordLinkResolveMode.Immediate,
            Action<T> assignLoadedValue = null)
        {
            if (!_entries.TryGetValue(name, out byte[] entry)
                || entry == null)
            {
                value = default;
                return;
            }

            string id = MemoryPackSerializer.Deserialize<string>(entry);
            if (id == null)
            {
                value = default;
                return;
            }

            if (resolveMode == RecordLinkResolveMode.Deferred)
            {
                if (assignLoadedValue == null)
                    throw new InvalidOperationException(
                        $"Deferred link '{name}' of type {typeof(T).Name} requires an assignment callback.");

                if (Context.Links.TryResolve(id, out T deferredValue, slot))
                {
                    value = deferredValue;
                    assignLoadedValue(deferredValue);
                    return;
                }

                Context.QueueDeferredLink(name, id, slot, assignLoadedValue);
                value = default;
                return;
            }

            if (!Context.Links.TryResolve(id, out T resolvedValue, slot))
            {
                throw new InvalidOperationException(
                    $"Unable to load link '{name}' of type {typeof(T).Name} with id '{id}'{FormatSlot(slot)}.");
            }

            value = resolvedValue;
            assignLoadedValue?.Invoke(resolvedValue);
        }

        private T CreateDefaultDeepStruct<T>() where T : struct, IRecordable
        {
            T defaultValue = new();
            var nested = new MemoryPackRecordReader(EmptyRecordBytes, Context);
            defaultValue.RecordData(nested);
            return defaultValue;
        }
    }

    private static string FormatSlot(string slot)
    {
        return string.IsNullOrEmpty(slot)
            ? string.Empty
            : $" in slot '{slot}'";
    }
}
