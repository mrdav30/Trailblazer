//=======================================================================
// NavigationOperationValidation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections.Diagnostics;

namespace Trailblazer.Pathing;

internal static class NavigationOperationValidation
{
    internal static void ValidateSchedule(long operationSequence, int effectiveFrame)
    {
        SwiftThrowHelper.ThrowIfArgument(operationSequence <= 0, nameof(operationSequence));
        SwiftThrowHelper.ThrowIfArgument(effectiveFrame < 0, nameof(effectiveFrame));
    }
}
