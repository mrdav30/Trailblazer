using FixedMathSharp;
using System.Collections.Generic;
using System.Numerics;
using Trailblazer.Utility.Coroutines;

namespace Trailblazer.AgentMotor.Locomotions
{
    [System.Serializable]
    public class PlatformLocomotion : ILocomotion
    {
        #region Constants

        public static readonly Fixed64 GlobalHeightAdjust = Fixed64.FromRaw(0x80000000L); // 0.5f;

        public static readonly MovementTransferState DefaultMovementTransfer = MovementTransferState.PermaTransfer;

        #endregion

        public bool IsEnabled;

        public MovementTransferState MovementTransfer = DefaultMovementTransfer;

        public bool IsOnPlatform => IsEnabled && CurrentPlatformMatrix != null;

        public bool IsNewPlatform { get; internal set; }

        public Fixed4x4 CurrentPlatformMatrix { get; internal set; }

        public Vector3d PlatformPosition { get; private set; }

        public FixedQuaternion PlatformRotation { get; private set; }

        public Fixed4x4 PreviousPlatformMatrix { get; internal set; }

        public Vector3d ActiveGlobalPoint { get; internal set; }

        public Vector3d ActiveLocalPoint { get; internal set; }

        public FixedQuaternion ActiveGlobalRotation { get; internal set; }

        public FixedQuaternion ActiveLocalRotation { get; internal set; }

        public Vector3d PlatformVelocity { get; internal set; }

        public bool IsApplyingInititalVelocity => IsOnPlatform
        && (MovementTransfer == MovementTransferState.InitTransfer || MovementTransfer == MovementTransferState.PermaTransfer);

        public bool IsPlatformVelocityEnforced => IsOnPlatform && MovementTransfer == MovementTransferState.PermaLocked;

        public bool DidPlatformChanged(Fixed4x4 groundMatrix)
        {
            if (CurrentPlatformMatrix == groundMatrix)
                return false;

            CurrentPlatformMatrix = groundMatrix;
            PreviousPlatformMatrix = groundMatrix;
            IsNewPlatform = true;
            if (CurrentPlatformMatrix != null)
            {
                PlatformPosition = CurrentPlatformMatrix.ExtractTranslation();
                PlatformRotation = CurrentPlatformMatrix.ExtractRotation();
            }
            else
            {
                PlatformPosition = default;
                PlatformRotation = default;
                PlatformVelocity = default;
            }
            return IsNewPlatform;
        }

        public void UpdatePlatformVelocity()
        {
            if (!IsNewPlatform)
            {
                Vector3d currentPoint = CurrentPlatformMatrix * ActiveLocalPoint;
                Vector3d previousPoint = PreviousPlatformMatrix * ActiveLocalPoint;
                Vector3d newVelocity = (currentPoint - previousPoint) / TrailblazerSettings.DeltaTime;
                PlatformVelocity = Vector3d.Lerp(PlatformVelocity, newVelocity, Fixed64.FromRaw(0x40000000L)); // ~0.25 lerp factor
            }

            PreviousPlatformMatrix = CurrentPlatformMatrix;
        }
    }
}