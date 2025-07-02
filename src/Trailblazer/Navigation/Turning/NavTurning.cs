using FixedMathSharp;
using System;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Navigation.Turning
{
    /// <summary>
    /// The Turn class manages the character's rotation and turning functionality.
    /// </summary>
    public class NavTurning
    {
        private static readonly Fixed64 _minTurnRequiredAngle = Fixed64.FromRaw(0x9520000L); // 0.036407470703125f * 2^32;
        private static readonly Fixed64 _arriveThresholdAngle = Fixed64.FromRaw(0x68DB9L); // 0.0001M;

        private bool _targetReached;

        public bool TargetReached => _targetReached;

        private FixedQuaternion _targetRotation;

        public FixedQuaternion TargetRotation => _targetRotation;

        public bool CanTurn = true;

        // The turning speed, in degrees per frame, determining how fast
        // the agent turns towards the target direction.
        // Adjust within a reasonable range for desired responsiveness.
        // For example, So if you set it to 1, your character will turn by 1 degree per frame.
        public Fixed64 TurnRate = Fixed64.One / 8;

        private bool _bufferStartTurn;

        private Vector3d _bufferTargetDirection;

        private FixedQuaternion _bufferTargetRotation;

        private Fixed64 _bufferInterpolation;

        private Fixed64 _collisionTurnThreshold;

        private bool _isColliding;

        public Action<FixedQuaternion> OnApplyRotation;

#nullable enable
        public Func<bool>? CanAutoTurn { get; set; } = null;
#nullable disable

        /// <summary>
        /// Creates a new <see cref="NavTurning"/> instance and initializes it with the provided navigator.
        /// </summary>
        /// <param name="radius">The radius of the navigator entity that this controller will manage.</param>
        /// <returns>A new instance of <see cref="NavMotor"/>.</returns>
        public static NavTurning CreateNew(Fixed64 radius) => new(radius);

        /// <summary>
        /// Initializes a new, empty instance of the <see cref="NavTurning"/> class.
        /// </summary>
        public NavTurning() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="NavTurning"/> class.
        /// </summary>
        /// <param name="radius">The radius of the navigator entity that this controller will manage.</param>
        public NavTurning(Fixed64 radius) => OnInitialize(radius);

        public void OnInitialize(Fixed64 radius)
        {
            _collisionTurnThreshold = radius / TrailblazerManager.FrameRate * Fixed64.Half;
            _collisionTurnThreshold *= _collisionTurnThreshold;

            _targetReached = true;
            _targetRotation = FixedQuaternion.Identity;
            _bufferStartTurn = false;
        }

        public void OnSimulate(ITurn navigator)
        {
            if (!CanTurn || _targetReached) return;

            // Calculate the axis of rotation
            Vector3d axis = Vector3d.Cross(_targetRotation.ToDirection(), navigator.Forward);

            // Create a rotation quaternion
            FixedQuaternion rotation = FixedQuaternion.AngleAxis(TurnRate, axis);

            // Calculate the angle between the current rotation * rotation and the target rotation
            Fixed64 angle = FixedQuaternion.Angle(navigator.Rotation * rotation, _targetRotation);

            if (angle <= _arriveThresholdAngle)
            {
                Arrive(navigator);
                return;
            }

            // If the angle is within a certain threshold,
            // apply the rotation to the body's current rotation
            FixedQuaternion targetRot = navigator.Rotation * rotation;
            OnApplyRotation?.Invoke(targetRot);
            navigator.ApplyRotation(targetRot);
        }

        public void OnLateSimulate(ITurn navigator)
        {
            if (!CanTurn) return;

            if (!_targetReached)
            {
                Fixed64 check = FixedQuaternion.Angle(navigator.Rotation, _targetRotation);
                if (check > _arriveThresholdAngle)
                    Arrive(navigator);
            }
            else
                CheckAutoTurn(navigator.Position, navigator.LastPosition, navigator.Forward);

            // check if there is a turn request waiting
            if (_bufferStartTurn)
            {
                _bufferStartTurn = false;
                StartTurnForNextFrame(navigator);
            }
        }

        private void CheckAutoTurn(Vector3d position, Vector3d lastPosition, Vector3d curDirection)
        {
            if (!_isColliding) return;
            _isColliding = false;

            //  autoturn direction will be culmination of positional changes
            if (!_targetReached || CanAutoTurn?.Invoke() == false)
                return;

            Vector3d delta = position - lastPosition;
            if (delta.SqrMagnitude < _collisionTurnThreshold)
                return;

            delta.Normalize();
            RequestTurnDirection(curDirection, delta);
        }

        /// <summary>
        /// Checks if the character needs to turn based on the angle between targetDirection and currentForward.
        /// </summary>
        /// <param name="targetDirection">Target direction vector.</param>
        /// <param name="currentForward">Current forward direction vector.</param>
        /// <param name="minAngle">The minimum angle threshold required to trigger a turn.</param>
        /// <returns>True if the character needs to turn, False otherwise.</returns>
        public bool NeedsTurn(
            Vector3d targetDirection,
            Vector3d currentForward,
            Fixed64? minAngle = null)
        {
            FixedQuaternion targetRotation = FixedQuaternion.FromDirection(targetDirection);
            FixedQuaternion currentRotation = FixedQuaternion.FromDirection(currentForward);

            Fixed64 angle = FixedQuaternion.Angle(currentRotation, targetRotation);
            bool withinTurn = angle <= (minAngle ?? _minTurnRequiredAngle);

            return !withinTurn;
        }

        /// <summary>
        /// Sets buffer rotation by converting target direction into a fixed quaternion
        /// </summary>
        /// <param name="curDirection">The current facing direction of the agent.</param>
        /// <param name="targetDirection">Th normalized target direction to turn to.</param>
        /// <param name="interpolation">Speed of rotation towards direction.</param>
        public void RequestTurnDirection(
            Vector3d curDirection,
            Vector3d targetDirection,
            Fixed64? interpolation = null)
        {
            if (!CanTurn || !NeedsTurn(curDirection, targetDirection))
                return;

            _bufferInterpolation = interpolation ?? Fixed64.Zero;
            _bufferStartTurn = true;
            _bufferTargetDirection = targetDirection;
            _bufferTargetRotation = FixedQuaternion.FromDirection(targetDirection);
        }

        /// <summary>
        /// Sets the target rotation and starts the turning process.
        /// </summary>
        private void StartTurnForNextFrame(ITurn navigator)
        {
            if (NeedsTurn(_bufferTargetDirection, navigator.Forward)) // Use an appropriate threshold here
            {
                _targetRotation = _bufferTargetRotation;
                _targetReached = false;
            }
            else
                Arrive(navigator);
        }

        /// <summary>
        /// Sets the character's rotation and stops the turning process.
        /// </summary>
        public void Arrive(ITurn navigator)
        {
            OnApplyRotation?.Invoke(_targetRotation);
            navigator.ApplyRotation(_targetRotation);
            StopTurn();
        }

        /// <summary>
        /// Stops the turning process.
        /// </summary>
        public void StopTurn() => _targetReached = true;

        public void HandleContact() => _isColliding = true;
    }
}
