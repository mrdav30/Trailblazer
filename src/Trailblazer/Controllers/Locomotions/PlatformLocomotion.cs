using FixedMathSharp;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// The state of the movement transfer.
    /// </summary>
    public enum MovementTransferState
    {
        /// <summary>
        /// The scout is not affected by velocity of the platform at all.
        /// </summary>
        None = 0,
        /// <summary>
        /// scout gets its initial velocity from the platform, then gradualy comes to a stop.
        /// </summary>
        InitTransfer = 1,
        /// <summary>
        /// scout gets its initial velocity from the platform, and keeps that velocity until landing.
        /// </summary>
        PermaTransfer = 2,
        /// <summary>
        /// scout is relative to the movement of the last touched platform and will move together with that platform.
        /// </summary>
        PermaLocked = 3
    }

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

        /// <summary>
        /// The state of the movement transfer.
        /// </summary>
        public MovementTransferState MovementTransfer = DefaultMovementTransfer;

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

        /// <summary>
        /// The active platform.
        /// </summary>
        public object ActivePlatform { get; set; }

        /// <summary>
        /// The active platform matrix.
        /// </summary>
        public Fixed4x4 ActiveMatrix { get; set; }

        /// <summary>
        /// The last platform matrix.
        /// </summary>
        public Fixed4x4 LastMatrix { get; set; }

        /// <summary>
        /// The active global point.
        /// </summary>
        public Vector3d ActiveGlobalPoint { get; set; }

        /// <summary>
        /// The active local point.
        /// </summary>
        public Vector3d ActiveLocalPoint { get; set; }

        /// <summary>
        /// The active global rotation.
        /// </summary>
        public FixedQuaternion ActiveGlobalRotation { get; set; }

        /// <summary>
        /// The active local rotation.
        /// </summary>
        public FixedQuaternion ActiveLocalRotation { get; set; }

        /// <summary>
        /// The velocity of the platform.
        /// </summary>
        public Vector3d ActiveVelocity { get; set; }

        /// <summary>
        /// This keeps track of our current velocity while we're not grounded
        /// </summary>
        public Vector3d FrameVelocity { get; set; }

        /// <summary>
        /// The current state of the platform locomotion.
        /// </summary>
        public HoldPlatformState State { get; private set; }

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
        public bool IsInteriaApplied => IsOnPlatform
            && (MovementTransfer == MovementTransferState.InitTransfer
                || MovementTransfer == MovementTransferState.PermaTransfer);

        /// <summary>
        /// Whether the scout is locked on to a platform.
        /// </summary>
        public bool IsLockedToPlatform => IsOnPlatform && MovementTransfer == MovementTransferState.PermaLocked;

        public bool IsHoldingPlatform => State != HoldPlatformState.Idle
;
        #endregion

        public void SetHoldPlatform(object platform)
        {
            HoldPlatform = platform;
            HoldPlatformFrames = 0;
            State = HoldPlatformState.Holding;
        }

        public bool UpdateHoldOnPlatform()
        {
            switch (State)
            {
                case HoldPlatformState.Holding:
                    {
                        HoldPlatformFrames--;
                        if (HoldPlatformFrames >= MaxHoldPlatformFrames)
                        {
                            State = HoldPlatformState.Release;
                            HoldPlatformFrames = 0;
                        }
                        return false;
                    }
                case HoldPlatformState.Release:
                    {
                        State = HoldPlatformState.Idle;
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
            ActiveMatrix = other.ActiveMatrix;
            LastMatrix = other.LastMatrix;
            ActiveGlobalPoint = other.ActiveGlobalPoint;
            ActiveGlobalRotation = other.ActiveGlobalRotation;
            ActiveLocalPoint = other.ActiveLocalPoint;
            ActiveLocalRotation = other.ActiveLocalRotation;
            ActiveVelocity = other.ActiveVelocity;
            FrameVelocity = other.FrameVelocity;
            State = other.State;
            HoldPlatform = other.HoldPlatform;
            HoldPlatformFrames = other.HoldPlatformFrames;
        }

        /// <inheritdoc cref="ITransientLocomotion.ClearState"/>
        public void ClearState()
        {
            IsNewPlatform = false;
            ActivePlatform = null;
            ActiveMatrix = Fixed4x4.Identity;
            LastMatrix = Fixed4x4.Identity;
            ActiveGlobalPoint = Vector3d.Zero;
            ActiveLocalPoint = Vector3d.Zero;
            ActiveGlobalRotation = FixedQuaternion.Identity;
            ActiveLocalRotation = FixedQuaternion.Identity;
            ActiveVelocity = Vector3d.Zero;
            State = HoldPlatformState.Idle;
            HoldPlatform = null;
            HoldPlatformFrames = 0;
        }
    }
}