using FixedMathSharp;
using System;
using Trailblazer.Controllers.Locomotions;

namespace Trailblazer.Controllers
{
    /// <summary>
    /// The events of the scout.
    /// </summary>
    public class ScoutEvents
    {
#nullable enable
        /// <summary>
        /// The action to set the position of the scout.
        /// </summary>
        public Action<Vector3d>? OnAddPositionDelta { get; set; } = null;
        
        /// <summary>
        /// The action to set the rotation of the scout.
        /// </summary>
        public Action<FixedQuaternion>? OnAddRotationDelta { get; set; } = null;

        /// <summary>
        /// The action to add a linear force to the scout.
        /// </summary>
        public Action<Vector3d>? OnAddLinearForce { get; set; } = null;

        /// <summary>
        /// The action to add an angular force to the scout.
        /// </summary>
        public Action<Vector3d>? OnAddAngularForce { get; set; } = null;

        /// <summary>
        /// The action to start the fall of the scout.
        /// </summary>
        public Action? OnStartFall { get; set; } = null;

        /// <summary>
        /// The action to stop the fall of the scout.
        /// </summary>
        public Action<Fixed64>? OnStopFall { get; set; } = null;

        /// <summary>
        /// The action to stop the fall of the scout.
        /// </summary>
        public Action? OnMaxFallHeightReached { get; set; } = null;

        /// <summary>
        /// The action to stop the fall of the scout.
        /// </summary>
        public Action<Fixed64>? OnDrowning { get; set; } = null;

        /// <summary>
        /// The action to stop the fall of the scout.
        /// </summary>
        public Func<bool>? CanAffordJump { get; set; } = null;

        /// <summary>
        /// The action to start the jump of the scout.  
        /// Notification sends <see cref="JumpLocomotion.AvoidGroundingTimer"/> to assist in preventing ground checks for the specified time.
        /// </summary>
        public Action<Fixed64>? OnStartJump { get; set; } = null;

        /// <summary>
        /// The action to stop the jump of the scout.
        /// </summary>
        public Action? OnStopJump { get; set; } = null;

        /// <summary>
        /// The action to land the scout.
        /// </summary>
        public Action? OnLandedFall { get; set; } = null;

        /// <summary>
        /// The action to start the water breach of the scout.
        /// </summary>
        public Action? OnStartWaterBreach { get; set; } = null;

        /// <summary>
        /// The action to stop the water breach of the scout.
        /// </summary>
        public Action? OnStopWaterBreach { get; set; } = null;

#nullable disable
    }
}
