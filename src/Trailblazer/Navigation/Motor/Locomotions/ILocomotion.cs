using Trailblazer.Support;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Defines the base interface for all locomotion modules that control specific movement behaviors.
/// </summary>
public interface ILocomotion
{
    /// <summary>
    /// Indicates whether this locomotion behavior is enabled.
    /// If disabled, its movement effects will not be applied.
    /// </summary>
    bool IsEnabled { get; set; }
}

public interface ITransientLocomotion : ILocomotion, ITransient { }
