using System.Collections.Generic;
using MemoryPack;

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
    public Dictionary<string, byte[]> Entries { get; set; } = new();
}
