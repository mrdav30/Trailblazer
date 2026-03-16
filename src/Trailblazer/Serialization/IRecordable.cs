namespace Trailblazer.Serialization;

/// <summary>
/// Defines a type that can record its serializable state through a chronicler pass.
/// </summary>
public interface IRecordable
{
    /// <summary>
    /// Records the current instance state to the provided chronicler.
    /// </summary>
    /// <param name="chronicler">The active chronicler pass.</param>
    void RecordData(IChronicler chronicler);
}
