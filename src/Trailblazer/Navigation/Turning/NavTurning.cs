using Chronicler;
using FixedMathSharp;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Navigation.Turning;

/// <summary>
/// The Turn class manages the character's rotation and turning functionality.
/// </summary>
public partial class NavTurning : IRecordable
{
    #region Constants

    /// <summary>
    /// The minimum physical angular difference (in degrees) required to initiate a turn.
    /// </summary>
    private static readonly Fixed64 _minTurnRequiredAngle =
        Fixed64.FromRaw(0x9520000L); // 0.036407470703125f * 2^32;

    /// <summary>
    /// The physical angular threshold (in degrees) below which a turn is considered complete.
    /// </summary>
    private static readonly Fixed64 _arriveThresholdAngle =
        Fixed64.FromRaw(0x68DB9L); // 0.0001M;

    /// <summary>
    /// Represents the default turn rate value used for rotation calculations.
    /// </summary>
    /// <remarks>
    /// This value is typically used as a standard or baseline turn rate in movement or rotation logic. 
    /// The specific value is one eighth of a full rotation, which may correspond to 45 degrees if a full rotation is considered 360 degrees.
    /// </remarks>
    public static readonly Fixed64 DefaultTurnRate = Fixed64.One / 8;

    #endregion

    #region Fields

    /// <summary>
    /// Whether this turning controller is currently allowed to perform turns.
    /// </summary>
    public bool CanTurn = true;

    /// <summary>
    /// The base turn rate, controlling how much rotation is applied per simulation step.
    /// </summary>
    public Fixed64 TurnRate = DefaultTurnRate;

    /// <summary>
    /// Buffered target rotation quaternion to apply on the next simulation frame.
    /// </summary>
    private FixedQuaternion? _pendingTarget;

    /// <summary>
    /// Custom interpolation factor to override the default TurnRate for the next turn operation.
    /// </summary>
    private Fixed64 _pendingInterpolation;

    /// <summary>
    /// Navigator radius cached so collision auto-turn thresholds can track frame-rate changes.
    /// </summary>
    private Fixed64 _radius;

    private bool _isInitialized;

    private TrailblazerWorldContext? _context;

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
    /// The desired target rotation quaternion that the object is turning toward.
    /// </summary>
    public FixedQuaternion TargetRotation { get; private set; }

    /// <summary>
    /// Gets the world context this turning controller is bound to, when explicitly bound.
    /// </summary>
    public TrailblazerWorldContext? Context => _context;

    #endregion

    #region Actions and Functions

    /// <summary>
    /// Optional predicate that determines whether an auto-turn is permitted after a collision.
    /// </summary>
    public Func<bool>? CanTurnOnCollision { get; set; } = null;

    #endregion

    /// <summary>
    /// Creates and initializes a context-bound <see cref="NavTurning"/> instance.
    /// </summary>
    public static NavTurning CreateNew(TrailblazerWorldContext context, Fixed64 radius) => new(context, radius);

    private NavTurning() { }

    /// <summary>
    /// Constructs and immediately initializes a context-bound <see cref="NavTurning"/>.
    /// </summary>
    public NavTurning(TrailblazerWorldContext context, Fixed64 radius)
    {
        BindContext(context);
        OnInitialize(radius);
    }

    /// <summary>
    /// Binds this turning controller to a world context.
    /// </summary>
    public void BindContext(TrailblazerWorldContext context)
    {
        Trailblazer.Pathing.PathRequestContextResolver.ThrowIfUnusable(context);
        _context = context;
    }

    private TrailblazerWorldContext RequireContext() =>
        _context ?? throw new InvalidOperationException("NavTurning requires an explicit TrailblazerWorldContext.");

    private int FrameRate => RequireContext().FrameRate;

    private Fixed64 DeltaTime => RequireContext().DeltaTime;

    /// <summary>
    /// Configures internal thresholds based on the object’s radius and resets turn state.
    /// </summary>
    public void OnInitialize(Fixed64 radius)
    {
        _radius = radius;
        _isInitialized = true;

        TargetReached = true;
        TargetRotation = FixedQuaternion.Identity;

        _pendingTarget = null;
        _pendingInterpolation = Fixed64.Zero;
        _isColliding = false;
    }

    /// <summary>
    ///Advances the object’s rotation toward the <see cref="TargetRotation"/>, handling both buffered and auto-turn logic.
    /// </summary>
    public bool TrySimulateTurn(
        Vector3d position,
        Vector3d lastPosition,
        Vector3d forward,
        FixedQuaternion rotation,
        out FixedQuaternion appliedRotation)
    {
        appliedRotation = FixedQuaternion.Identity;
        // 1) Preconditions
        if (!_isInitialized)
            throw new InvalidOperationException(
              "NavTurning.OnInitialize must be called before SimulateTurn()");
        if (!CanTurn) return false;

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
                    position,
                    lastPosition,
                    forward);
                return false;    // only here do we exit early
            }
        }

        // 3) Mid-turn (or just consumed buffer): do the Slerp
        var t = _pendingInterpolation > Fixed64.Zero
                    ? _pendingInterpolation
                    : TurnRate * DeltaTime;
        t = FixedMath.Clamp(t, Fixed64.Zero, Fixed64.One);

        var next = FixedQuaternion.Slerp(rotation, TargetRotation, t);

        if (FixedQuaternion.Angle(next, TargetRotation) <= _arriveThresholdAngle)
        {
            // we’ve arrived
            appliedRotation = TargetRotation;
            StopTurn();
        }
        else
            appliedRotation = next;

        return true;
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
        if (delta.MagnitudeSquared < GetCollisionTurnThreshold()
            || !TargetReached
            || (CanTurnOnCollision?.Invoke() == false))
        {
            // keep _isColliding true so we retry next frame
            return;
        }

        // 2) now we know we’ll actually turn
        _isColliding = false;
        delta.NormalizeInPlace();
        RequestTurnDirection(curDirection, delta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fixed64 GetCollisionTurnThreshold()
    {
        Fixed64 threshold = _radius / FrameRate * Fixed64.Half;
        return threshold * threshold;
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
    public static bool NeedsTurn(
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
    /// Signals that a collision has occurred and an auto-turn should be evaluated on the next call to <see cref="TrySimulateTurn"/>.
    /// </summary>
    public void NotifyCollision()
    {
        _isColliding = true;
    }
}
