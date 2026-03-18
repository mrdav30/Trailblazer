using System;
using System.IO;
using System.Text;
using System.Text.Json;
using SwiftCollections;

namespace Trailblazer.Serialization;

/// <summary>
/// Serializes <see cref="IRecordable"/> state graphs to and from JSON through the chronicler API.
/// </summary>
public static class JsonRecordSerializer
{
    private static readonly JsonSerializerOptions _defaultOptions = CreateDefaultOptions();

    /// <summary>
    /// Serializes the current state of a recordable instance into JSON.
    /// </summary>
    public static string Serialize(IRecordable target, bool writeIndented = false)
        => Serialize(target, context: null, writeIndented);

    /// <summary>
    /// Serializes the current state of a recordable instance into JSON.
    /// </summary>
    public static string Serialize(IRecordable target, ChronicleContext context, bool writeIndented = false)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        context ??= new ChronicleContext();

        JsonSerializerOptions options = writeIndented
            ? CreateIndentedOptions()
            : _defaultOptions;

        var chronicler = new JsonRecordWriter(options, context);
        target.RecordData(chronicler);
        return chronicler.ToJson();
    }

    /// <summary>
    /// Loads JSON state into an existing recordable instance.
    /// </summary>
    public static void Populate(IRecordable target, string json)
        => Populate(target, json, context: null);

    /// <summary>
    /// Loads JSON state into an existing recordable instance.
    /// </summary>
    public static void Populate(IRecordable target, string json, ChronicleContext context)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Serialized JSON must not be null or empty.", nameof(json));

        context ??= new ChronicleContext();

        using var chronicler = new JsonRecordReader(json, _defaultOptions, context);
        target.RecordData(chronicler);
        context.ResolveDeferredLinks();
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        return new JsonSerializerOptions()
        {
            IncludeFields = true
        };
    }

    private static JsonSerializerOptions CreateIndentedOptions()
    {
        return new JsonSerializerOptions(_defaultOptions)
        {
            WriteIndented = true
        };
    }

    private sealed class JsonRecordWriter : IChronicler
    {
        private readonly SwiftDictionary<string, string> _entries = new(8, StringComparer.Ordinal);
        private readonly JsonSerializerOptions _options;

        public JsonRecordWriter(JsonSerializerOptions options, ChronicleContext context)
        {
            _options = options;
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public ChronicleContext Context { get; }

        public SerializationMode Mode => SerializationMode.Saving;

        public void LookValue<T>(ref T value, string name, T defaultValue = default)
        {
            if(value == null || value.Equals(defaultValue))
                return;
            _entries[name] = JsonSerializer.Serialize(value, _options);
        }

        public void LookDeep<T>(ref T value, string name) where T : class, IRecordable
        {
            if (value == null)
            {
                _entries[name] = "null";
                return;
            }

            var nested = new JsonRecordWriter(_options, Context);
            value.RecordData(nested);
            _entries[name] = nested.ToJson();
        }

        public void LookDeepStruct<T>(ref T value, string name) where T : struct, IRecordable
        {
            var nested = new JsonRecordWriter(_options, Context);
            value.RecordData(nested);
            _entries[name] = nested.ToJson();
        }

        public void LookNullableDeep<T>(ref T? value, string name) where T : struct, IRecordable
        {
            if (!value.HasValue)
                return;

            T nestedValue = value.Value;
            var nested = new JsonRecordWriter(_options, Context);
            nestedValue.RecordData(nested);
            _entries[name] = nested.ToJson();
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

            _entries[name] = JsonSerializer.Serialize(id, _options);
        }

        public string ToJson()
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions() { Indented = _options.WriteIndented }))
            {
                writer.WriteStartObject();

                foreach (var entry in _entries)
                {
                    writer.WritePropertyName(entry.Key);
                    using var document = JsonDocument.Parse(entry.Value);
                    document.RootElement.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private sealed class JsonRecordReader : IChronicler, IDisposable
    {
        private readonly JsonDocument _document;
        private readonly JsonElement _root;
        private readonly JsonSerializerOptions _options;

        public JsonRecordReader(string json, JsonSerializerOptions options, ChronicleContext context)
        {
            _document = JsonDocument.Parse(json);
            _root = _document.RootElement;
            _options = options;
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public ChronicleContext Context { get; }

        public SerializationMode Mode => SerializationMode.Loading;

        public void LookValue<T>(ref T value, string name, T defaultValue = default)
        {
            if (!_root.TryGetProperty(name, out JsonElement entry))
            {
                value = defaultValue;
                return;
            }

            if (entry.ValueKind == JsonValueKind.Null)
            {
                value = defaultValue;
                return;
            }

            T loadedValue = JsonSerializer.Deserialize<T>(entry.GetRawText(), _options);
            value = loadedValue == null ? defaultValue : loadedValue;
        }

        public void LookDeep<T>(ref T value, string name) where T : class, IRecordable
        {
            if (!_root.TryGetProperty(name, out JsonElement entry) || entry.ValueKind == JsonValueKind.Null)
                return;

            if (value == null)
                throw new InvalidOperationException(
                    $"Unable to load '{name}' because {typeof(T).Name} must already be instantiated for a deep chronicler load.");

            using var nested = new JsonRecordReader(entry.GetRawText(), _options, Context);
            value.RecordData(nested);
        }

        public void LookDeepStruct<T>(ref T value, string name) where T : struct, IRecordable
        {
            value = CreateDefaultDeepStruct<T>();

            if (!_root.TryGetProperty(name, out JsonElement entry) || entry.ValueKind == JsonValueKind.Null)
                return;

            using var nested = new JsonRecordReader(entry.GetRawText(), _options, Context);
            value.RecordData(nested);
        }

        public void LookNullableDeep<T>(ref T? value, string name) where T : struct, IRecordable
        {
            if (!_root.TryGetProperty(name, out JsonElement entry) || entry.ValueKind == JsonValueKind.Null)
            {
                value = null;
                return;
            }

            T nestedValue = CreateDefaultDeepStruct<T>();
            using var nested = new JsonRecordReader(entry.GetRawText(), _options, Context);
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
            if (!_root.TryGetProperty(name, out JsonElement entry)
                || entry.ValueKind == JsonValueKind.Null)
            {
                value = default;
                return;
            }

            string id = JsonSerializer.Deserialize<string>(entry.GetRawText(), _options);
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

        public void Dispose()
        {
            _document.Dispose();
        }

        private T CreateDefaultDeepStruct<T>() where T : struct, IRecordable
        {
            T defaultValue = new();
            using var nested = new JsonRecordReader("{}", _options, Context);
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
