namespace Trailblazer.Serialization;

/// <summary>
/// Controls whether a recorded link must resolve during the current load pass or may resolve after the full graph has loaded.
/// </summary>
public enum RecordLinkResolveMode
{
    /// <summary>
    /// The link must resolve immediately while the current <c>RecordData(...)</c> call is running.
    /// </summary>
    Immediate,

    /// <summary>
    /// The link may resolve after the current <c>RecordData(...)</c> graph finishes loading.
    /// </summary>
    Deferred
}
