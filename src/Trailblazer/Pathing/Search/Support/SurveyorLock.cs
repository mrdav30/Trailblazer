namespace Trailblazer.Pathing;

internal static class SurveyorLock
{
    public static readonly object GlobalLock = new();
}
