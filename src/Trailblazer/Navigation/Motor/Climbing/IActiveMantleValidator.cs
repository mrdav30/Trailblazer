//=======================================================================
// IActiveMantleValidator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Provides optional host-owned validation for an already active mantle.
/// </summary>
public interface IActiveMantleValidator
{
    /// <summary>
    /// Attempts to validate whether the current active mantle may continue this frame.
    /// </summary>
    bool TryValidateActiveMantle(
        TransitState currentState,
        ActiveMantleState activeMantle,
        out MantleValidationSnapshot snapshot);
}
