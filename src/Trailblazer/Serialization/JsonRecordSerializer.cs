using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Trailblazer.Serialization;

/// <summary>
/// Serializes <see cref="IRecordable"/> state graphs to and from JSON through the chronicler API.
/// </summary>
public static class JsonRecordSerializer
{
    private static readonly JsonSerializerOptions _defaultOptions = CreateDefaultOptions();

    /// <summary>
    /// Serializes the current state of an exposable instance into JSON.
    /// </summary>
    public static string Serialize(IRecordable target, bool writeIndented = false)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        JsonSerializerOptions options = writeIndented
            ? CreateIndentedOptions()
            : _defaultOptions;

        var chronicler = new JsonRecordWriter(options);
        target.RecordData(chronicler);
        return chronicler.ToJson();
    }

    /// <summary>
    /// Loads JSON state into an existing exposable instance.
    /// </summary>
    public static void Populate(IRecordable target, string json)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Serialized JSON must not be null or empty.", nameof(json));

        using var chronicler = new JsonRecordReader(json, _defaultOptions);
        target.RecordData(chronicler);
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
        private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);
        private readonly JsonSerializerOptions _options;

        public JsonRecordWriter(JsonSerializerOptions options)
        {
            _options = options;
        }

        public SerializationMode Mode => SerializationMode.Saving;

        public void LookValue<T>(ref T value, string name, T defaultValue = default)
        {
            _entries[name] = JsonSerializer.Serialize(value, _options);
        }

        public void LookDeep<T>(ref T value, string name) where T : class, IRecordable
        {
            if (value == null)
            {
                _entries[name] = "null";
                return;
            }

            var nested = new JsonRecordWriter(_options);
            value.RecordData(nested);
            _entries[name] = nested.ToJson();
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

        public JsonRecordReader(string json, JsonSerializerOptions options)
        {
            _document = JsonDocument.Parse(json);
            _root = _document.RootElement;
            _options = options;
        }

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
            if (!_root.TryGetProperty(name, out JsonElement entry)
                || entry.ValueKind == JsonValueKind.Null)
                return;

            if (value == null)
                throw new InvalidOperationException(
                    $"Unable to load '{name}' because {typeof(T).Name} must already be instantiated for a deep chronicler load.");

            using var nested = new JsonRecordReader(entry.GetRawText(), _options);
            value.RecordData(nested);
        }

        public void Dispose()
        {
            _document.Dispose();
        }
    }
}
