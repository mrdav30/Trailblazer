//=======================================================================
// NavMotor.Serialization.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
