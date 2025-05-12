using System;

namespace Trailblazer.Navigator.Motor
{
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

    /// <summary>
    /// Defines locomotion modules with state that may change per frame and require synchronization.
    /// </summary>
    public interface ITransientLocomotion : ILocomotion
    {
        /// <summary>
        /// Synchronizes transient properties from another locomotion instance.
        /// </summary>
        /// <param name="other">The locomotion instance to sync with.</param>
        void SyncState(ITransientLocomotion other);

        /// <summary>
        /// Clears transient properties, resetting them to default values.
        /// </summary>
        void ClearState();
    }
}
