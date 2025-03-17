namespace Trailblazer.Controllers.Locomotions
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
        /// Synchronizes the transient state of this locomotion with another instance.
        /// </summary>
        /// <param name="other">The locomotion instance to sync with.</param>
        void SyncState(ITransientLocomotion other);

        /// <summary>
        /// Resets the locomotion state, clearing any active effects.
        /// </summary>
        void ClearState();
    }
}
