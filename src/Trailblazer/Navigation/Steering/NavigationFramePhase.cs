//=======================================================================
// NavigationFramePhase.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation.Steering;

internal static class NavigationFramePhase
{
    internal static void ValidateConfiguration(int intervalFrames, int? phase)
    {
        if (phase is not int value)
            return;
        ChronicleGuard.ThrowIfArgumentOutOfRange(intervalFrames <= 0, intervalFrames, nameof(intervalFrames));
        ChronicleGuard.ThrowIfArgumentOutOfRange(value < 0 || value >= intervalFrames, value, nameof(phase));
    }

    internal static int NextDelay(long frame, int interval, int phase)
    {
        int delay = phase - (int)(frame % interval);
        return delay > 0 ? delay : delay + interval;
    }
}
