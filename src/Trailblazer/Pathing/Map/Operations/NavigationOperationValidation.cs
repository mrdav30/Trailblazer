//=======================================================================
// NavigationOperationValidation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

internal static class NavigationOperationValidation
{
    internal static void ValidateSchedule(long operationSequence, long effectiveFrame)
    {
        ChronicleGuard.ThrowIfArgument(operationSequence <= 0, nameof(operationSequence));
        ChronicleGuard.ThrowIfArgument(effectiveFrame < 0, nameof(effectiveFrame));
    }
}
