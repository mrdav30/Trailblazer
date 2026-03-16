namespace Trailblazer.Serialization;

/// <summary>
/// Defines a type that can expose its serializable state through a chronicler pass.
/// </summary>
public interface IRecordable
{
    /// <summary>
    /// Exposes the current instance state to the provided chronicler.
    /// </summary>
    /// <param name="chronicler">The active chronicler pass.</param>
    void RecordData(IChronicler chronicler);
}
