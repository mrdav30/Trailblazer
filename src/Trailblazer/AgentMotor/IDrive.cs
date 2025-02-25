using FixedMathSharp;
using System;

namespace Trailblazer.AgentMotor
{
    public struct BodyData
    {
        public Vector3d WorldPosition { get; }

        public FixedQuaternion VisualRotation { get; }

        public Vector3d LinearVelocity { get; }

        public Fixed64 Mass { get; }
    }

    public struct ColliderData
    {
        public Vector3d Center;

        public Vector3d ScaledSize;

        public Fixed64 Radius;
    }

    public struct GroundData
    {
        // Driver needs to check ground state prior to calling simulate
        public bool IsGrounded { get; set; }

        public Vector3d GroundNormal { get; }

        public Fixed4x4 GroundMatrix { get; }
    }

    public interface IDrive
    {
        BodyData BodyData { get; }

        ColliderData ColliderData { get; }

        GroundData GroundData { get; }

        bool CanAffordToJump();

        /// <summary>
        /// Call this any time after visual position or rotation has been mutated
        /// </summary>
        Action OnDidMove { get; set; }

        Action<Vector3d> OnSetPosition { get; set; }

        Action<FixedQuaternion> OnSetRotation { get; set; }

        Action<Vector3d> OnAddForce { get; set; }

        Action OnStartFall { get; set; }

        Action<Fixed64> OnStopFall { get; set; }

        Action OnMaxFallHeightReached { get; set; }

        Action<Fixed64> OnDrowning { get; set; }

        Action OnStartJump { get; set; }

        Action OnStopJump { get; set; }

        Action OnStartWaterBreach { get; set; }

        Action OnStopWaterBreach { get; set; }

        Action<Fixed64> OnSkipGroundingCheckTimer { get; set; }
    }
}
