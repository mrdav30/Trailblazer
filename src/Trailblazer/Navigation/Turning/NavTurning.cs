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
        #region Constants

        /// <summary>
        /// The minimum angular difference (in radians) required to initiate a turn.
        /// </summary>
        private static readonly Fixed64 _minTurnRequiredAngle =
            Fixed64.FromRaw(0x9520000L); // 0.036407470703125f * 2^32;

        /// <summary>
        /// The angular threshold (in radians) below which a turn is considered complete.
        /// </summary>
        private static readonly Fixed64 _arriveThresholdAngle =
            Fixed64.FromRaw(0x68DB9L); // 0.0001M;

        #endregion

        #region Fields

        /// <summary>
        /// Whether this turning controller is currently allowed to perform turns.
        /// </summary>
        public bool CanTurn = true;

        /// <summary>
        /// The base turn rate, controlling how much rotation is applied per simulation step.
        /// </summary>
        public Fixed64 TurnRate = Fixed64.One / 8;

        /// <summary>
        /// Buffered target rotation quaternion to apply on the next simulation frame.
        /// </summary>
        private FixedQuaternion? _pendingTarget;

        /// <summary>
        /// Custom interpolation factor to override the default TurnRate for the next turn operation.
        /// </summary>
        private Fixed64 _pendingInterpolation;

        /// <summary>
        /// Squared movement distance threshold used to determine when to trigger an auto-turn after a collision.
        /// </summary>
        private Fixed64 _collisionTurnThreshold;

        /// <summary>
        /// Flag indicating that a collision has occurred and an auto-turn should be considered.
        /// </summary>
        private bool _isColliding;

        #endregion

        #region Properties

        /// <summary>
        /// Indicates whether the current turn operation has completed (i.e., the target rotation has been reached).
        /// </summary>
        public bool TargetReached { get; private set; }

        /// <summary>
        /// The desired target rotation quaternion that the navigator is turning toward.
        /// </summary>
        public FixedQuaternion TargetRotation { get; private set; }

        #endregion

        #region Actions and Functions

        /// <summary>
        /// Event invoked whenever a new rotation quaternion should be applied to the navigator.
        /// </summary>
        public Action<FixedQuaternion> OnApplyRotation;

        /// <summary>
        /// Optional predicate that determines whether an auto-turn is permitted after a collision.
        /// </summary>
#nullable enable
        public Func<bool>? CanTurnOnCollision { get; set; } = null;
#nullable disable

        #endregion

        /// <summary>
        /// Creates and initializes a new <see cref="NavTurning"/> instance for a navigator of the given radius.
        /// </summary>
        public static NavTurning CreateNew(Fixed64 radius) => new(radius);

        /// <summary>
        /// Constructs a <see cref="NavTurning"/> without initializing collision thresholds.
        /// </summary>
        public NavTurning() { }

        /// <summary>
        /// Constructs and immediately initializes a <see cref="NavTurning"/> with the given navigator radius.
        /// </summary>
        public NavTurning(Fixed64 radius) => OnInitialize(radius);

        /// <summary>
        /// Configures internal thresholds based on the navigator’s radius and resets turn state.
        /// </summary>
        public void OnInitialize(Fixed64 radius)
        {
            _collisionTurnThreshold = radius / TrailblazerManager.FrameRate * Fixed64.Half;
            _collisionTurnThreshold *= _collisionTurnThreshold;

            TargetReached = true;
            TargetRotation = FixedQuaternion.Identity;

            _pendingTarget = null;
        }

        /// <summary>
        ///Advances the navigator’s rotation toward the <see cref="TargetRotation"/>, handling both buffered and auto-turn logic.
        /// </summary>
        public void SimulateTurn(ITurn navigator)
        {
            // 1) Preconditions
            if (_collisionTurnThreshold == Fixed64.Zero)
                throw new InvalidOperationException(
                  "NavTurning.OnInitialize must be called before SimulateTurn()");
            if (!CanTurn) return;

            // 2) If we’re idle (finished last turn):
            if (TargetReached)
            {
                // 2a) Phase-2: consume a buffered turn (if any) *and continue* to rotation
                if (_pendingTarget.HasValue)
                {
                    TargetRotation = _pendingTarget.Value;
                    _pendingTarget = null;
                    TargetReached = false;
                    // **no return** here — we want to immediately start turning
                }
                // 2b) Phase-1: no buffer yet, check for collision and buffer, then bail
                else
                {
                    CheckAutoTurn(
                        navigator.Position,
                        navigator.LastPosition,
                        navigator.Forward);
                    return;    // only here do we exit early
                }
            }

            // 3) Mid-turn (or just consumed buffer): do the Slerp
            var t = _pendingInterpolation > Fixed64.Zero
                        ? _pendingInterpolation
                        : TurnRate * TrailblazerManager.DeltaTime;
            t = FixedMath.Clamp(t, Fixed64.Zero, Fixed64.One);

            var next = FixedQuaternion.Slerp(navigator.Rotation, TargetRotation, t);

            if (FixedQuaternion.Angle(next, TargetRotation) <= _arriveThresholdAngle)
            {
                // we’ve arrived
                OnApplyRotation?.Invoke(TargetRotation);
                navigator.ApplyRotation(TargetRotation);
                StopTurn();
            }
            else
            {
                OnApplyRotation?.Invoke(next);
                navigator.ApplyRotation(next);
            }
        }

        /// <summary>
        /// Checks if a recent collision and sufficient movement warrant buffering an auto-turn, and buffers it if so.
        /// </summary>
        private void CheckAutoTurn(
            Vector3d position,
            Vector3d lastPosition,
            Vector3d curDirection)
        {
            if (!_isColliding) return;

            // 1) compute delta first
            Vector3d delta = position - lastPosition;
            if (delta.SqrMagnitude < _collisionTurnThreshold
                || !TargetReached
                || (CanTurnOnCollision?.Invoke() == false))
            {
                // keep _isColliding true so we retry next frame
                return;
            }

            // 2) now we know we’ll actually turn
            _isColliding = false;
            delta.Normalize();
            RequestTurnDirection(curDirection, delta);
        }

        /// <summary>
        /// Buffers a new turn request toward the given target direction if the required angle exceeds the minimum threshold.
        /// </summary>
        public void RequestTurnDirection(
            Vector3d curDirection,
            Vector3d targetDirection,
            Fixed64? interpolation = null)
        {
            if (!NeedsTurn(curDirection, targetDirection))
                return;

            _pendingInterpolation = interpolation ?? Fixed64.Zero;
            _pendingTarget = FixedQuaternion.FromDirection(targetDirection);
        }

        /// <summary>
        /// Determines whether the angular difference between the current forward and a desired target direction exceeds the minimum threshold.
        /// </summary>
        public bool NeedsTurn(
            Vector3d currentForward,
            Vector3d targetDirection,
            Fixed64? minAngle = null)
        {
            FixedQuaternion currentRotation = FixedQuaternion.FromDirection(currentForward);
            FixedQuaternion targetRotation = FixedQuaternion.FromDirection(targetDirection);

            Fixed64 angle = FixedQuaternion.Angle(currentRotation, targetRotation);
            bool withinTurn = angle <= (minAngle ?? _minTurnRequiredAngle);

            return !withinTurn;
        }

        /// <summary>
        /// Marks the current turn operation as complete, allowing new turns to be requested.
        /// </summary>
        public void StopTurn() => TargetReached = true;

        /// <summary>
        /// Signals that a collision has occurred and an auto-turn should be evaluated on the next call to <see cref="SimulateTurn"/>.
        /// </summary>
        public void NotifyCollision()
        {
            _isColliding = true;
        }
    }
}
