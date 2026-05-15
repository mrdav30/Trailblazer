using Chronicler;

namespace Trailblazer.Navigation.Turning;

public partial class NavTurning
{
    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref CanTurn, "CanTurn", true);
        RecordValues.Look(chronicler, ref TurnRate, "TurnRate", DefaultTurnRate);
    }
}
