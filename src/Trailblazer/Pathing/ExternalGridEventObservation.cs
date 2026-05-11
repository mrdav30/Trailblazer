namespace Trailblazer.Pathing;

internal readonly struct ExternalGridEventObservation
{
    public ExternalGridEventObservation(
        ExternalGridEventSignature signature,
        int identicalEventStreak)
    {
        Signature = signature;
        IdenticalEventStreak = identicalEventStreak;
    }

    public ExternalGridEventSignature Signature { get; }

    public int IdenticalEventStreak { get; }
}
