using FixedMathSharp;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// The state of the platform locomotion.
    /// </summary>
    public enum HoldPlatformState
    {
        Idle = 0,
        Holding = 1,
        Release = 2
    }

    /// <summary>
    /// A helper class for the platform locomotion.
    /// </summary>
    [System.Serializable]
    public class PlatformLocomotion : ITransientLocomotion
    {
        #region Constants

        /// <summary>
        /// The global height adjust.
        /// </summary>
        public static readonly Fixed64 DefaultHeightAdjust = Fixed64.FromRaw(0x80000000L); // 0.5f;

        /// <summary>
        /// The default movement transfer state.
        /// </summary>
        public static readonly MovementTransferState DefaultMovementTransfer = MovementTransferState.PermaTransfer;

        public const int MaxHoldPlatformFrames = 2;

        #endregion

        #region Configuration State

        /// <summary>
        /// Whether the platform locomotion is enabled.
        /// </summary>
        private bool _isEnabled = true;

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

        public bool IsNewPlatform { get; set; }

        public MovementTransferState MovementTransfer { get; set; }

        /// <summary>
        /// The active platform.
        /// </summary>
        public object ActivePlatform { get; set; }

        /// <summary>
        /// The active platform matrix.
        /// </summary>
        public Fixed4x4 ActiveTransform { get; set; } = Fixed4x4.Identity;

        /// <summary>
        /// The last platform matrix.
        /// </summary>
        public Fixed4x4 LastTransform { get; set; } = Fixed4x4.Identity;

        /// <summary>
        /// The global point of the scout on the platform.
        /// </summary>
        public Vector3d ScoutGlobalPoint { get; set; }

        /// <summary>
        /// The local point of the scout on the platform.
        /// </summary>
        public Vector3d ScoutLocalPoint { get; set; }

        /// <summary>
        /// The global rotation of the scout on the platform.
        /// </summary>
        public FixedQuaternion ScoutGlobalRotation { get; set; } = FixedQuaternion.Identity;

        /// <summary>
        /// The local rotation of the scout on the platform.
        /// </summary>
        public FixedQuaternion ScoutLocalRotation { get; set; } = FixedQuaternion.Identity;

        /// <summary>
        /// The velocity of the platform.
        /// </summary>
        public Vector3d ActiveVelocity { get; set; }

        /// <summary>
        /// This keeps track of the platform's velocity while we're not grounded
        /// </summary>
        public Vector3d FrameForce { get; set; }

        /// <summary>
        /// The current state of the platform locomotion.
        /// </summary>
        public HoldPlatformState HoldState { get; private set; }

        /// <summary>
        /// The last active platform.
        /// </summary>
        public object HoldPlatform { get; private set; }

        /// <summary>
        /// The number of frames the platform has been held.
        /// </summary>
        public int HoldPlatformFrames { get; private set; }

        /// <summary>
        /// Whether the scout is on a platform.
        /// </summary>
        public bool IsOnPlatform => IsEnabled && ActivePlatform != null;

        /// <summary>
        /// Whether the initial velocity has been applied.
        /// </summary>
        public bool IsPlatformInteriaApplied => IsOnPlatform
            && ActiveVelocity != Vector3d.Zero
            && (MovementTransfer == MovementTransferState.InitTransfer
                || MovementTransfer == MovementTransferState.PermaTransfer);

        /// <summary>
        /// Whether the scout is locked on to a platform.
        /// </summary>
        public bool IsLockedToPlatform => IsOnPlatform && MovementTransfer == MovementTransferState.PermaLocked;

        public bool IsHoldingPlatform => HoldState != HoldPlatformState.Idle
;
        #endregion

        public void SetHoldPlatform(object platform)
        {
            HoldPlatform = platform;
            HoldPlatformFrames = 0;
            HoldState = HoldPlatformState.Holding;
        }

        public bool UpdateHoldOnPlatform()
        {
            switch (HoldState)
            {
                case HoldPlatformState.Holding:
                    {
                        HoldPlatformFrames--;
                        if (HoldPlatformFrames >= MaxHoldPlatformFrames)
                        {
                            HoldState = HoldPlatformState.Release;
                            HoldPlatformFrames = 0;
                        }
                        return false;
                    }
                case HoldPlatformState.Release:
                    {
                        HoldState = HoldPlatformState.Idle;
                        HoldPlatform = null;
                        return true;
                    }
                default:
                    return false;
            }
        }

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
            ActiveVelocity = other.ActiveVelocity;
            FrameForce = other.FrameForce;
            HoldState = other.HoldState;
            HoldPlatform = other.HoldPlatform;
            HoldPlatformFrames = other.HoldPlatformFrames;
        }

        /// <inheritdoc cref="ITransientLocomotion.ClearState"/>
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
            ActiveVelocity = Vector3d.Zero;
            FrameForce = Vector3d.Zero;
            HoldState = HoldPlatformState.Idle;
            HoldPlatform = null;
            HoldPlatformFrames = 0;
        }
    }
}