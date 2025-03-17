using FixedMathSharp;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// Handles movement adjustments when the scout is standing on a moving platform or surface.
    /// </summary>
    /// <remarks>
    /// This locomotion system tracks platform velocity, rotation, and movement transfer behavior.
    /// It allows the scout to inherit motion from platforms and supports different transfer states.
    /// </remarks>
    [System.Serializable]
    public class PlatformLocomotion : ITransientLocomotion
    {
        #region Constants

        /// <summary>
        /// Default height adjustment applied when standing on a moving platform.
        /// </summary>
        public static readonly Fixed64 DefaultHeightAdjust = Fixed64.FromRaw(0x80000000L); // 0.5f;

        /// <summary>
        /// Maximum number of frames the scout can remain attached to a platform before release.
        /// </summary>
        public const int MaxHoldPlatformFrames = 2;

        #endregion

        #region Configuration State

        /// <summary>
        /// Determines whether platform locomotion is enabled.
        /// </summary>
        private bool _isEnabled = true;

        /// <summary>
        /// The height offset applied when interacting with moving platforms.
        /// </summary>
        public Fixed64 HeightAdjust = DefaultHeightAdjust;

        #endregion

        #region Transient State

        /// <inheritdoc cref="ILocomotion.IsEnabled"/>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                if (!_isEnabled)
                    ClearState();
            }
        }

        /// <summary>
        /// Indicates whether the scout has just landed on a new platform.
        /// </summary>
        public bool IsNewPlatform { get; set; }

        /// <summary>
        /// Defines how movement is transferred from the platform to the scout.
        /// </summary>
        public MovementTransferState MovementTransfer { get; set; }
        
        /// <summary>
        /// The platform object the scout is currently standing on.
        /// </summary>
        public object ActivePlatform { get; set; }

        /// <summary>
        /// The transformation matrix of the active platform.
        /// </summary>
        public Fixed4x4 ActiveTransform { get; set; } = Fixed4x4.Identity;

        /// <summary>
        /// The transformation matrix of the last known platform.
        /// </summary>
        public Fixed4x4 LastTransform { get; set; } = Fixed4x4.Identity;

        /// <summary>
        /// The global position of the scout on the platform.
        /// </summary>
        public Vector3d ScoutGlobalPoint { get; set; }

        /// <summary>
        /// The local position of the scout relative to the platform.
        /// </summary>
        public Vector3d ScoutLocalPoint { get; set; }

        /// <summary>
        /// The global rotation of the scout on the platform.
        /// </summary>
        public FixedQuaternion ScoutGlobalRotation { get; set; } = FixedQuaternion.Identity;

        /// <summary>
        /// The local rotation of the scout relative to the platform.
        /// </summary>
        public FixedQuaternion ScoutLocalRotation { get; set; } = FixedQuaternion.Identity;

        /// <summary>
        /// The velocity of the platform.
        /// </summary>
        public Vector3d PlatformVelocity { get; set; }

        /// <summary>
        /// The last known platform velocity when the scout is airborne.
        /// </summary>
        public Vector3d FramePlatformVelocity { get; set; }

        /// <summary>
        /// Indicates whether the scout is currently holding onto a platform.
        /// </summary>
        public bool IsHoldingPlatform { get; private set; }

        /// <summary>
        /// The last known platform the scout was attached to.
        /// </summary>
        public object HoldPlatform { get; private set; }

        /// <summary>
        /// The number of frames the scout has been holding onto a platform.
        /// </summary>
        public int HoldPlatformFrames { get; private set; }

        /// <summary>
        /// Indicates whether the scout is currently standing on a platform.
        /// </summary>
        public bool IsOnPlatform => IsEnabled && ActivePlatform != null;

        /// <summary>
        /// Indicates whether platform inertia (initial velocity transfer) has been applied.
        /// </summary>
        public bool IsPlatformInteriaApplied => IsOnPlatform
            && PlatformVelocity != Vector3d.Zero
            && (MovementTransfer == MovementTransferState.InitTransfer
                || MovementTransfer == MovementTransferState.PermaTransfer);

        /// <summary>
        /// Indicates whether the scout is locked to a platform and will move with it.
        /// </summary>
        public bool IsLockedToPlatform => IsOnPlatform && MovementTransfer == MovementTransferState.PermaLocked;

        #endregion

        /// <summary>
        /// Assigns the scout to a platform, initiating a hold state.
        /// </summary>
        /// <param name="platform">The platform object to attach to.</param>
        public void SetHoldPlatform(object platform)
        {
            HoldPlatform = platform;
            HoldPlatformFrames = 0;
            IsHoldingPlatform = true;
        }

        /// <summary>
        /// Updates the platform hold state, releasing the hold if the hold duration expires.
        /// </summary>
        /// <returns>True if the scout should detach from the platform; otherwise, false.</returns>
        public bool CanReleaseHoldOnPlatform()
        {
            if (!IsHoldingPlatform)
                return false;

            HoldPlatformFrames++;
            if(HoldPlatformFrames > MaxHoldPlatformFrames)
            {
                HoldPlatformFrames = 0;
                if (HoldPlatform != ActivePlatform)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Synchronizes platform movement state with another <see cref="PlatformLocomotion"/> instance.
        /// </summary>
        /// <param name="locomotion">The locomotion instance to sync with.</param>
        public void SyncState(ITransientLocomotion locomotion)
        {
            if (locomotion is not PlatformLocomotion other) return;

            IsNewPlatform = other.IsNewPlatform;
            ActivePlatform = other.ActivePlatform;
            ActiveTransform = other.ActiveTransform;
            LastTransform = other.LastTransform;
            ScoutGlobalPoint = other.ScoutGlobalPoint;
            ScoutGlobalRotation = other.ScoutGlobalRotation;
            ScoutLocalPoint = other.ScoutLocalPoint;
            ScoutLocalRotation = other.ScoutLocalRotation;
            PlatformVelocity = other.PlatformVelocity;
            FramePlatformVelocity = other.FramePlatformVelocity;
            IsHoldingPlatform = other.IsHoldingPlatform;
            HoldPlatform = other.HoldPlatform;
            HoldPlatformFrames = other.HoldPlatformFrames;
        }

        /// <summary>
        /// Resets platform-related state, clearing platform references and velocities.
        /// </summary>
        public void ClearState()
        {
            IsNewPlatform = false;
            ActivePlatform = null;
            ActiveTransform = Fixed4x4.Identity;
            LastTransform = Fixed4x4.Identity;
            ScoutGlobalPoint = Vector3d.Zero;
            ScoutLocalPoint = Vector3d.Zero;
            ScoutGlobalRotation = FixedQuaternion.Identity;
            ScoutLocalRotation = FixedQuaternion.Identity;
            PlatformVelocity = Vector3d.Zero;
            FramePlatformVelocity = Vector3d.Zero;
            IsHoldingPlatform = false;
            HoldPlatform = null;
            HoldPlatformFrames = 0;
        }
    }
}