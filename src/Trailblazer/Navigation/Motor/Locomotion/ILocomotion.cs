//=======================================================================
// ILocomotion.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Chronicler;
using Trailblazer.Support;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Defines the base interface for all locomotion modules that control specific movement behaviors
/// with frame-local runtime state that can be synced or cleared.
/// </summary>
public interface ILocomotion : ITransient, IRecordable
{
    /// <summary>
    /// Indicates whether this locomotion behavior is enabled.
    /// If disabled, its movement effects will not be applied.
    /// </summary>
    bool IsEnabled { get; set; }
}
