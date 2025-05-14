using System;
using FixedMathSharp;
using System.Diagnostics;
using GridForge.Grids;
using SwiftCollections;
using Trailblazer.Pathing;
using Trailblazer.Navigation.Motor;
using System.Runtime.CompilerServices;

namespace Trailblazer.Navigation
{
    /// <summary>
    /// Base class representing a navigator, responsible for handling movement, traversal state, and simulation flow.
    /// </summary>
    /// <remarks>
    /// This class acts as a bridge between the simulation logic (`ScoutController`) and the entity's external representation.  
    /// It defines common traversal behaviors and lifecycle methods that can be extended by concrete implementations.
    /// </remarks>
    [Serializable]
    public class Navigator : INavigate, IAvoidanceBody
    {
        // Stop multipliers determine accuracy required for stopping on the destination
        public static readonly Fixed64 DefaultDirectStop = Fixed64.FromRaw(0x40000000L); // 0.25f;
        public static readonly Fixed64 DecelerationMultiplier = (Fixed64)10f;
        public static readonly Fixed64 DefaultFootPositionAdjust = new Fixed64(0.25f);

        private static readonly int _stuckFrameThreshold = TrailblazerManager.FrameRate / 4;
        private static readonly int _stuckRepathTries = 4;

        private static readonly int AutoPauseStopTime = TrailblazerManager.FrameRate / 8;

        #region Serialized

        /// <summary>
        /// The size of this unit in worldspace.
        /// </summary>
        /// <remarks>
        /// Note: Add a little padding to manevour around blockers
        /// </remarks>
        public int RoverSize = 1;

        /// <summary>
        /// Adjustment factor for the foot position, used to determine ground contact points.
        /// </summary>
        public Fixed64 FootPositionAdjust = DefaultFootPositionAdjust;

        public bool IsAbleToMove = true;

        public bool IsAbleToTurn = true;

        /// <summary>
        /// Disable if unit doesn't need to find path, i.e. flying
        /// </summary>
        public bool IsAbleToPathfind = true;

        #endregion

        public IGuide TrailGuide { get; set; }

        /// <inheritdoc cref="INavigate.Motor"/>
        public NavMotor Motor { get; protected set; }

        /// <inheritdoc cref="INavigate.Events"/>
        public NavEvents Events { get; protected set; }

        /// <inheritdoc cref="INavigate.Position"/>
        public Vector3d Position { get; protected set; }

        /// <inheritdoc cref="INavigate.Rotation"/>
        public FixedQuaternion Rotation { get; protected set; } = FixedQuaternion.Identity;

        public Fixed64 Radius => RoverSize * Fixed64.Half;

        public Vector3d AveragePosition { get; set; } // used to check if stuck

        /// <summary>
        /// The current traversal condition of the scout, including medium (ground, air, water) and surface level.
        /// </summary>
        public TraversalCondition TraversalCondition { get; protected set; }

        public Vector3d Destination { get; set; }

        public Vector3d LastDestination { get; set; }

        protected Node _currentNode;

        protected Node _destinationNode;

        public bool IsMoving { get; private set; }

        public bool IsAvoidingLeft { get; set; }

        public bool IsStuck { get; private set; }

        /// <summary>
        /// Has this unit arrived at destination?
        /// </summary>
        public bool IsAtDestination { get; private set; }

        public Fixed64 StopMultiplier { get; set; } = DefaultDirectStop;

        private bool _isSearchingForPath;

        public Vector3d Velocity { get; set; }

        private Fixed64 _timescaledAcceleration;

        private Fixed64 _timescaledDeceleration;

        private bool _isDecelerating;

        /// <summary>
        /// How far we move each update
        /// </summary>
        protected Fixed64 _distanceToMove;

        /// <summary>
        /// How far away the agent stops from the target
        /// </summary>
        private Fixed64 _closingDistance;

        protected bool _isOnLineOfSightPath;

        private bool _allowUnwalkableDestination;

        private bool _viableDestination;

        private int _stuckFrameCount;

        private int _repathTries;

        #region Group Properties

        public int MovementGroupID { get; set; } = -1;

        public bool IsInGroup => MovementGroupID != -1;

        #endregion

        #region Auto stopping properites

        public int StoppedFrameCount { get; private set; }

        private int _autoStopFrameCount;

        public bool CanAutoStop => _autoStopFrameCount <= 0;

        #endregion

        /// <summary>
        /// Stores the movement request for the next traversal cycle.
        /// </summary>
        public TraversalRequest MoveRequest { get; protected set; }

        public bool IsControlled { get; protected set; }

        public bool IsFollowingWaypoint => TrailGuide != null && TrailGuide.HasPath && TrailGuide.HasWaypoints;

        /// <summary>
        /// Initializes the navigator by setting up its defaults, events, traversal state, and movement controller.
        /// </summary>
        public virtual void Initialize(
            Vector3d startingPosition, 
            Vector3d? initialVelocity,
            Fixed64? bodyRadius, 
            TraversalCondition traversalCondition)
        {
            Position = startingPosition;

            // Fatter objects can afford to land imprecisely
            _closingDistance = FixedMath.Round(bodyRadius ?? Fixed64.Half + GlobalGridManager.NodeSize);

            MovementGroupID = -1;

            StoppedFrameCount = 0;
            _autoStopFrameCount = 0;

            StopMultiplier = DefaultDirectStop;

            _isSearchingForPath = false;
            _isOnLineOfSightPath = false;
            IsMoving = false;
            IsAvoidingLeft = false;

            _viableDestination = false;
            Destination = Vector3d.Zero;

            IsStuck = false;
            _stuckFrameCount = 0;
            _repathTries = 0;

            IsAtDestination = true;
            AveragePosition = Position;

            TraversalCondition = traversalCondition;
            Motor = NavMotor.CreateNew(this, TraversalCondition);
            Motor.SetVelocity(initialVelocity ?? Vector3d.Zero);

            TrailGuide = null;
            MoveRequest = TraversalRequest.Empty;

            Events = new();
        }

        // TODO: we probably shouldn't let player controlled characters move onto unwalkable nodes...
        public virtual void RequestControlledMovement(TraversalRequest request)
        {
            IsMoving = true;
            IsAtDestination = false;
            IsStuck = false;

            StoppedFrameCount = 0;
            _stuckFrameCount = 0;

            MoveRequest = request;

            // TODO: release back to pool if exists?
            TrailGuide = null;

            IsControlled = true;
        }

        public virtual void RequestTrailGuide(
            Vector3d destination,
            IGuide guide,
            TrekRate rate = TrekRate.Stationary,
            bool isRequestingJump = false,
            bool allowUnwalkableEndNode = false,
            int groupId = -1)
        {
            if (guide == null)
                ThrowHelper.ThrowArgumentNullException(nameof(guide));

            // TODO: maybe this is where we can check from a cache pool of trail guides that hold a hash key value of from/destination/roversize
            TrailGuide = guide;
            TrailGuide.OnSetup();

            MoveRequest.Direction = default;
            MoveRequest.IsRequestingJump = isRequestingJump;
            MoveRequest.Rate = rate;

            IsControlled = false;

            _allowUnwalkableDestination = allowUnwalkableEndNode;

            _isOnLineOfSightPath = false;

            IsMoving = true;
            IsAtDestination = false;
            StoppedFrameCount = 0;
            IsStuck = false;
            _stuckFrameCount = 0;

            IsAvoidingLeft = false;

            MovementGroupID = groupId;

            _viableDestination = false;
            Destination = Vector3d.Zero;
            // if size requires consideration, use old next-best-node system
            // also a catch in case GetEndNode returns null
            if (RoverSize <= 1 && NodeFinder.GetEndNode(Position, destination, out _destinationNode, _allowUnwalkableDestination)
                || NodeFinder.GetClosestNodeForSize(Position, destination, RoverSize, out _destinationNode, _allowUnwalkableDestination))
            {
                _repathTries = 0;

                _viableDestination = true;
                Destination = _destinationNode.WorldPosition;

                if (IsInGroup)
                    _isSearchingForPath = true;
                else
                    _isSearchingForPath = false;

                Events.OnStartMove?.Invoke();

                return;
            }

            // no viable destination found
            Arrive();
        }

        /// <summary>
        /// Updates the scout’s traversal state, including its current medium and surface information.
        /// </summary>
        /// <param name="medium">The traversal medium (e.g., ground, air, water).</param>
        /// <param name="surfaceLevel">The vertical surface level, if applicable.</param>
        /// <param name="surfaceCondition">The ground state data, if applicable.</param>
        /// <param name="ceilingLevel">The vertical ceiling level, if applicable.</param>
        public virtual void SetTraversalCondition(
            TraversalMedium? medium = null,
            Fixed64? surfaceLevel = null,
            GroundCondition? surfaceCondition = null,
            Fixed64? ceilingLevel = null)
        {
            TraversalCondition.Medium = medium ?? TraversalCondition.Medium;
            TraversalCondition.SurfaceLevel = surfaceLevel ?? TraversalCondition.SurfaceLevel;
            TraversalCondition.GroundState = surfaceCondition ?? TraversalCondition.GroundState;
            TraversalCondition.CeilingLevel = ceilingLevel ?? TraversalCondition.CeilingLevel;
        }

        // Used for AI that already have a path and want to jump
        public virtual void ToggleJumpStatus(bool status)
        {
            if (!IsControlled) return;
            MoveRequest.IsRequestingJump = status;
        }

        // Used for AI that already have a path and want to change their rate of speed
        public virtual void SetTraversalSpeed(TrekRate rate)
        {
            if (!IsControlled) return;
            MoveRequest.Rate = rate;
        }

        public virtual void OnSimulate()
        {
            if (!IsAbleToMove)
                return;

            if (IsMoving)
            {
                if (IsControlled)
                {
                    ProcessControlledMovement();
                    return;
                }

                if (!_viableDestination)
                    return;

                // check if agent has to pathfind, otherwise straight path to rely on destination
                if (IsAbleToPathfind)
                    RequestPathFromTrailGuide();

                FindTargetDirection();
                if (MoveRequest.Direction != Vector3d.Zero)
                    FollowTrailGuide();
            }
            else
            {
                //Slowin' down
                if (Velocity.SqrMagnitude > Fixed64.Zero)
                {
                    _isDecelerating = true;
                    Velocity += AdjustVelocityForTimeScale(Vector3d.Zero);

                }
                else
                    _isDecelerating = false;
            }

            _autoStopFrameCount--;
            AveragePosition = Vector3d.Lerp(AveragePosition, Position, Fixed64.Half);
        }

        /// <summary>
        /// Finalizes traversal by updating movement calculations and applying corrections.
        /// </summary>
        /// <remarks>
        /// Should be called after physics bodies apply velocity changes
        /// </remarks>
        public virtual void OnVisualize()
        {
            Motor.FinishFrameTraversal(this, TraversalCondition);
            UpdateTimeScaling();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateTimeScaling()
        {
            _timescaledAcceleration = Motor.Locomotions.Move.Speed > Fixed64.Zero
                ? ((Motor.Locomotions.Move.Acceleration * Motor.Locomotions.Move.Speed) / TrailblazerManager.FrameRate).Magnitude
                : Fixed64.Zero;
            _timescaledDeceleration = _timescaledAcceleration > Fixed64.Zero
                ? _timescaledAcceleration * DecelerationMultiplier
                : Fixed64.Zero;
        }

        // Needs to be called every frame from input system
        protected virtual void ProcessControlledMovement()
        {
            if (IsAbleToTurn)
                Events.OnStartTurn?.Invoke(MoveRequest.Direction); //TODO: integrate this...

            Motor.Traverse(MoveRequest);
            MoveRequest.Reset();

            // TODO: this may not be required anymore...
            // call this after velocity changes
            //Vector3d previousVelocity = Velocity;
            //Velocity += AdjustVelocityForTimeScale(previousVelocity);
        }

        // For AI pathfinding, only needs to be called once until agent destination is reached
        protected virtual void RequestPathFromTrailGuide()
        {
            if (!_isSearchingForPath)
                return;

            if (RoverSize <= 1 && !NodeFinder.GetStartNode(Position, Destination, out _currentNode, _allowUnwalkableDestination)
                || !NodeFinder.GetClosestNodeForSize(Position, Destination, RoverSize, out _currentNode, _allowUnwalkableDestination))
            {
                Debug.Write("Agent is on an invalid position!");
                return;
            }

            _isSearchingForPath = false;
            if (_currentNode.SpawnToken == _destinationNode.SpawnToken && _repathTries >= 1)
            {
                Arrive();
                return;
            }

            _isOnLineOfSightPath = false;
            if (!PathingManager.NeedsPath(Position, Destination, RoverSize, _allowUnwalkableDestination))
            {
                // no path required
                // TODO: releaes the trailguide back to a pool
                TrailGuide = null;
                _isOnLineOfSightPath = true;
                return;
            }

            TrailGuide?.RequestMovementPath(Position, Destination, RoverSize);
        }

        protected virtual void FindTargetDirection()
        {
            if (_isOnLineOfSightPath)
                MoveRequest.Direction = Destination - Position;
            else if (TrailGuide != null && TrailGuide.HasPath)
                MoveRequest.Direction = TrailGuide.GetMovementDirection(Position);
            else
            {
                Debug.Write("No vialable movement direction found.");
                MoveRequest.Direction = Vector3d.Zero;
                return;
            }

            // This is now the direction we want to be travelling in 
            MoveRequest.Direction.Normalize(out _distanceToMove);

            // Calculate steering and flocking forces for all agents
            if (IsInGroup)
                MoveRequest.Direction += NavSteering.ComputeGroupSteering(Position, Velocity.Magnitude);

            // Avoid any intersection agents!
            MoveRequest.Direction += NavSteering.CalculateAvoidanceForce(this);
        }

        protected virtual void FollowTrailGuide()
        {
            Fixed64 stuckThreshold = _timescaledAcceleration > Fixed64.Zero ? _timescaledAcceleration / TrailblazerManager.FrameRate : Fixed64.Zero;
            Fixed64 slowDistance = _timescaledDeceleration > Fixed64.Zero ? _timescaledDeceleration / Velocity.Magnitude : Fixed64.Zero;

            Fixed64 moveAmount = FixedMath.Clamp01(MoveRequest.Direction.x.Abs() + MoveRequest.Direction.z.Abs());

            if (IsAbleToTurn)
                Events.OnStartTurn?.Invoke(MoveRequest.Direction); //TODO: integrate this...

            if(!IsFollowingWaypoint)
            {
                if (_distanceToMove < _closingDistance * StopMultiplier || !IsStuck && moveAmount == Fixed64.Zero)
                {
                    Arrive();
                    //TODO: Don't skip this frame of slowing down
                    return;
                }

                if (_distanceToMove <= slowDistance && _distanceToMove > _closingDistance * StopMultiplier)
                {
                    Fixed64 closingSpeed = _distanceToMove / slowDistance;

                    MoveRequest.Direction *= closingSpeed;
                    _isDecelerating = true;
                    // Reduce occurence of units preventing other units from reaching destination
                    stuckThreshold *= 4;
                }
            }

            CheckMovementStatus(stuckThreshold);

            Motor.Traverse(MoveRequest);
            MoveRequest.Reset();

            // TODO: this may not be required anymore...
            // call this after velocity changes
            //Vector3d previousVelocity = Velocity;
            //Velocity += AdjustVelocityForTimeScale(previousVelocity);
        }

        protected virtual void CheckMovementStatus(Fixed64 stuckThreshold)
        {
            _stuckFrameCount++;

            if (!CanAutoStop)
                return;

            // If unit has not moved stuckThreshold in a frame, it's stuck
            if (Vector3d.SqrDistance(Position, AveragePosition) <= (stuckThreshold * stuckThreshold))
            {
                if (_stuckFrameCount > _stuckFrameThreshold)
                {
                    if (_repathTries < _stuckRepathTries)
                    {
                        Debug.WriteLine("Stuck Agent!");

                        if (IsInGroup)
                            MovementGroupID = -1;  // Attempt to repath agent by themselves

                        // If we have a path, try to move to the next waypoint
                        if (IsFollowingWaypoint)
                        {
                            TrailGuide.MoveToNextWaypoint();
                            _repathTries++;
                            _stuckFrameCount = 0;
                            return;
                        }
                        else
                        {
                            IsAvoidingLeft = false;
                            _isSearchingForPath = true;
                            _isOnLineOfSightPath = false;

                            // Reset the guide and have them try a new path (don't pool a bad path)
                            TrailGuide?.Reset();
                        }

                        _repathTries++;
                    }
                    else
                    {
                        Debug.WriteLine("Stuck Agent arriving!");
                        // we've tried to many times, we stuck stuck
                        IsStuck = true;
                        Arrive();
                        return;
                    }

                    _stuckFrameCount = 0;
                }
            }
            else
            {
                IsStuck = false;

                if (_stuckFrameCount > 0)
                    _stuckFrameCount -= 1;

                _repathTries = 0;
            }

            if (!IsFollowingWaypoint) return;

            if (_distanceToMove < _closingDistance && Vector3d.Dot(Position, MoveRequest.Direction) < Fixed64.Epsilon
                || _distanceToMove < _closingDistance * GlobalGridManager.NodeSize)
            {
                TrailGuide?.MoveToNextWaypoint();
            }
        }

        // TODO: call this before setting velocity
        protected Vector3d AdjustVelocityForTimeScale(Vector3d desiredVelocity)
        {
            //The velocity change we want
            Vector3d velocityChange = desiredVelocity - Velocity;
            Fixed64 adjustFastMag = velocityChange.SqrMagnitude;

            //  Cap acceleration vector magnitude
            Fixed64 accel = _isDecelerating ? _timescaledDeceleration : _timescaledAcceleration;
            if (adjustFastMag > accel * (accel))
            {
                Fixed64 mag = FixedMath.Sqrt(adjustFastMag >> FixedMath.SHIFT_AMOUNT_I);
                //Convert to a force
                velocityChange *= accel / mag;
            }

            return velocityChange;
        }

        public void Arrive()
        {
            StopMove();

            _autoStopFrameCount = 0;
            _stuckFrameCount = 0;

            IsAtDestination = true;

            Events.OnArrive?.Invoke();
        }

        public virtual void StopMove()
        {
            if (!IsMoving)
                return;

            if (MovementGroupID != -1)
                MovementGroupID = -1;

            IsMoving = false;
            IsAvoidingLeft = false;
            StoppedFrameCount = 0;

            MoveRequest.Reset();

            _isSearchingForPath = false;
            _isOnLineOfSightPath = false;

            // TODO: return the trail guide?
            TrailGuide = null;

            Events.OnStopMove?.Invoke();
        }

        /// <summary>
        /// Returns the world-space position of the navigator’s foot, adjusted for proper ground contact.
        /// </summary>
        /// <returns>The adjusted foot position in world space.</returns>
        public virtual Vector3d GetFootPosition()
        {
            return Position + Vector3d.Down * FootPositionAdjust;
        }

        public void PauseAutoStop()
        {
            _autoStopFrameCount = AutoPauseStopTime;
        }
    }
}