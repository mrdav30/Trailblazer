using System.Collections.Generic;
using System;
using System.Linq;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// Manages locomotion states and behaviors for the <see cref="ScoutController"/>.
    /// </summary>
    /// <remarks>
    /// This class coordinates multiple locomotion types, ensuring that movement states are properly managed.
    /// </remarks>
    [System.Serializable]
    public class LocomotionHandler
    {
        /// <summary>
        /// Determines whether the scout has control over movement input.
        /// </summary>
        public bool IsInControl = true;

        #region Locomotions

        /// <summary>
        /// Handles general movement, including speed limits, acceleration, and velocity calculations.
        /// </summary>
        public MoveLocomotion Move = new();

        /// <summary>
        /// Manages movement when interacting with moving platforms or surfaces.
        /// </summary>
        /// <remarks>
        /// This locomotion maintains platform velocity tracking and movement transfer states.
        /// </remarks>
        public PlatformLocomotion Platform = new();

        /// <summary>
        /// Controls the airborne state when a jump is executed successfully.
        /// </summary>
        /// <remarks>
        /// This locomotion governs jump height, cooldown timing, and jump force calculations.
        /// </remarks>
        public JumpLocomotion Jump = new();

        /// <summary>
        /// Handles the scout’s falling behavior when downward momentum is detected.
        /// </summary>
        /// <remarks>
        /// This locomotion tracks fall distance, applies landing impact logic, and determines if a scout is free-falling.
        /// </remarks>
        public FallLocomotion Fall = new();

        /// <summary>
        /// Manages movement when sliding down steep surfaces.
        /// </summary>
        /// <remarks>
        /// This locomotion determines when the scout should slide and how much control it has over movement during the slide.
        /// </remarks>
        public SlideLocomotion Slide = new();

        /// <summary>
        /// Handles movement when the scout is in water, including buoyancy and water resistance.
        /// </summary>
        /// <remarks>
        /// This locomotion tracks swim speed, dive time, and breath management.
        /// </remarks>
        public SwimLocomotion Swim = new();

        #endregion

        /// <summary>
        /// Synchronizes locomotion states with another <see cref="LocomotionHandler"/> instance.
        /// </summary>
        /// <remarks>
        /// This ensures that all locomotion modules maintain consistent movement behavior when synchronizing states,
        /// which is useful for rollback systems or deterministic simulations.
        /// </remarks>
        /// <param name="other">The locomotion handler instance to sync with.</param>
        public void SyncState(LocomotionHandler other)
        {
            IsInControl = other.IsInControl;

            foreach (var locomotion in GetLocomotions())
            {
                if (locomotion.IsEnabled)
                    locomotion.SyncState(other.GetLocomotion(locomotion.GetType()));
            }
        }

        /// <summary>
        /// Clears the transient state of all locomotion modules.
        /// </summary>
        /// <remarks>
        /// This method resets movement states without altering locomotion configurations,
        /// ensuring a clean reset of position, velocity, and state-based properties.
        /// </remarks>
        public void ClearStateAll()
        {
            foreach (var locomotion in GetLocomotions())
            {
                if (locomotion.IsEnabled)
                    locomotion.ClearState();
            }
        }

        /// <summary>
        /// Gets all locomotion instances in the handler.
        /// </summary>
        public IEnumerable<ITransientLocomotion> GetLocomotions()
        {
            yield return Move;
            yield return Platform;
            yield return Jump;
            yield return Fall;
            yield return Swim;
            yield return Slide;
        }

        /// <summary>
        /// Retrieves a locomotion instance of a specific type from the handler.
        /// </summary>
        public ITransientLocomotion GetLocomotion(Type type)
        {
            return GetLocomotions().FirstOrDefault(l => l.GetType() == type);
        }
    }
}
