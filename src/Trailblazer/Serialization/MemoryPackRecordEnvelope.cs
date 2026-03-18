using System;
using MemoryPack;
using SwiftCollections;

namespace Trailblazer.Serialization;

/// <summary>
/// Stores named field payloads for the MemoryPack chronicler transport.
/// </summary>
[MemoryPackable]
internal sealed partial class MemoryPackRecordEnvelope
{
    /// <summary>
    /// Serialized payloads keyed by record name.
    /// </summary>
    public SwiftDictionary<string, byte[]> Entries { get; set; } = new(8, StringComparer.Ordinal);
}
