namespace Trailblazer.Serialization;

/// <summary>
/// Resolves stable identifiers for external or runtime-owned objects that should not be serialized inline.
/// </summary>
public interface IRecordLinkResolver<T>
{
    /// <summary>
    /// Attempts to produce a stable identifier for the provided value.
    /// </summary>
    bool TryGetReferenceId(T value, out string id);

    /// <summary>
    /// Attempts to resolve a previously recorded identifier back into a runtime value.
    /// </summary>
    bool TryResolveReference(string id, out T value);
}
