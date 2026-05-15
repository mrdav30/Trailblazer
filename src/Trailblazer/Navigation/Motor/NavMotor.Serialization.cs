using Chronicler;

namespace Trailblazer.Navigation.Motor;

public partial class NavMotor
{
    #region Serialization

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        TrekCondition currentCondition = CurrentState?.ToTrekCondition() ?? new TrekCondition();
        TrekCondition? previousCondition = CurrentState?.PreviousState;

        RecordDeep.Look(chronicler, ref _handler, "Handler");
        RecordValues.Look(chronicler, ref currentCondition, "CurrentCondition");
        RecordValues.Look(chronicler, ref previousCondition, "PreviousCondition");
        RecordValues.Look(chronicler, ref _isInitialized, "IsInitialized", false);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            CurrentState ??= new(currentCondition, previousCondition);
            CurrentState.Update(currentCondition, previousCondition);
            AbortTraversalFrame();
        }
    }

    #endregion
}
