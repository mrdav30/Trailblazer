namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// A base interface for all locomotion classes.
    /// </summary>
    public interface ILocomotion
    {
        /// <summary>
        /// Whether the locomotion is enabled.
        /// </summary>
        bool IsEnabled { get; set; }
    }

    public interface ITransientLocomotion : ILocomotion
    {
        void SyncState(ITransientLocomotion other);

        /// <summary>
        /// Resets the locomotion.
        /// </summary>
        void ClearState();
    }
}
