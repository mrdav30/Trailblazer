using Chronicler.Timing;

namespace Trailblazer.Tests;

internal static class TimingTestUtility
{
    // Boundary setup only: no public live-world rewind API is required by these tests.
    internal static void SetTime(TrailblazerWorldContext context, long frame,
        ChronicleTimestamp elapsed = default)
    {
        var owner = ReflectionUtility.GetPrivateField<TrailblazerClock>(context, "_clock");
        var timeline = ReflectionUtility.GetPrivateField<ChronicleClock>(owner, "_timeline");
        typeof(ChronicleClock).GetProperty(nameof(ChronicleClock.FrameCount))!.SetValue(timeline, frame);
        typeof(ChronicleClock).GetProperty(nameof(ChronicleClock.ElapsedTime))!.SetValue(timeline, elapsed);
    }
}
