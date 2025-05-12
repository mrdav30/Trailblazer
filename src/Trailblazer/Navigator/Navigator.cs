using System;
using FixedMathSharp;
using System.Diagnostics;
using GridForge.Grids;
using SwiftCollections;
using Trailblazer.Pathing;

namespace Trailblazer.Navigator
{
    [Serializable]
    public class Navigator : IAvoidanceBody
    {
        // Stop multipliers determine accuracy required for stopping on the destination
        public static readonly Fixed64 DefaultDirectStop = Fixed64.FromRaw(0x40000000L); // 0.25f;

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

        public Fixed64 Speed = Fixed64.One * 4;

        public Fixed64 Acceleration = Fixed64.One * 4;

        public bool IsAbleToMove = true;

        public bool IsAbleToTurn = true;

        /// <summary>
        /// Disable if unit doesn't need to find path, i.e. flying
        /// </summary>
        public bool IsAbleToPathfind = true;

        #endregion

        public IGuide TrailGuide { get; set; }

        public Fixed64 Radius => RoverSize * Fixed64.Half;

        public Vector3d Position { get; set; }

        protected Node _currentNode;

        public Vector3d AveragePosition { get; set; } // used to check if stuck

        public Vector3d Destination { get; set; }

        public Vector3d LastDestination { get; set; }

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

        protected Vector3d _targetDirection;

        public Vector3d Velocity { get; set; }

        private Vector3d _desiredVelocity;

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

        private Fixed64 _stuckTolerance;

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

        #region Actions

        public event Action OnMovementRequestProcessed;

        public event Action<Vector3d> OnStartTurn;

        /// <summary>
        /// Called when unit arrives at destination
        /// </summary>
        public event Action OnArrive;

        /// <summary>
        /// Called whenever movement is stopped
        /// </summary>
        public event Action OnStopMove;

        #endregion

        public virtual void Setup(Vector3d startingPosition, Fixed64 agentRadius, IGuide guide)
        {
            // TODO: if guide is null, assume isPlayerControlled
            if (guide == null)
                ThrowHelper.ThrowArgumentNullException(nameof(guide));

            TrailGuide = guide;

            Position = startingPosition;

            _timescaledAcceleration = (Acceleration * Speed) / TrailblazerManager.FrameRate;
            // Cleaner stops with more deceleration
            _timescaledDeceleration = _timescaledAcceleration * 4;
            // Fatter objects can afford to land imprecisely
            _closingDistance = agentRadius;
            _stuckTolerance = ((_closingDistance * Speed) >> FixedMath.SHIFT_AMOUNT_I) / TrailblazerManager.FrameRate;
            _stuckTolerance *= _stuckTolerance;

            TrailGuide.OnSetup();
        }

        public virtual void Initialize()
        {
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

            TrailGuide.OnInitialize();
        }

        public virtual void ProcessMovementRequest(Vector3d destination, bool allowUnwalkableEndNode = false, int groupId = -1)
        {
            _isOnLineOfSightPath = false;

            IsMoving = true;
            IsAtDestination = false;

            //TODO: If next-best-node, autostop more easily
            //Also implement stopping sooner based on distanceToMove
            StoppedFrameCount = 0;
            _stuckFrameCount = 0;
            _repathTries = 0;

            _allowUnwalkableDestination = allowUnwalkableEndNode;

            MovementGroupID = groupId;

            _viableDestination = false;
            Destination = Vector3d.Zero;
            // if size requires consideration, use old next-best-node system
            // also a catch in case GetEndNode returns null
            if (RoverSize <= 1 && NodeFinder.GetEndNode(Position, destination, out _destinationNode, _allowUnwalkableDestination)
                || NodeFinder.GetClosestNodeForSize(Position, destination, RoverSize, out _destinationNode, _allowUnwalkableDestination))
            {
                _viableDestination = true;
                Destination = _destinationNode.WorldPosition;
            }

            if (IsInGroup)
                _isSearchingForPath = true;
            else
                _isSearchingForPath = false;

            OnMovementRequestProcessed?.Invoke();
        }

        public virtual void Simulate()
        {
            if (!IsAbleToMove)
                return;

            if (IsMoving)
            {
                // check if agent has to pathfind, otherwise straight path to rely on destination
                if (IsAbleToPathfind)
                    ValidateMovementPath();

                SetMovementDirection();
                SetMovementState();
            }
            else
            {
                //Slowin' down
                if (Velocity.SqrMagnitude > Fixed64.Zero)
                {
                    _isDecelerating = true;
                    Velocity += GetAdjustedVelocity(Vector3d.Zero);

                }
                else
                    _isDecelerating = false;
            }

            _autoStopFrameCount--;
            AveragePosition = Vector3d.Lerp(AveragePosition, Position, Fixed64.Half);
        }

        public virtual void ValidateMovementPath()
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
            if (!_viableDestination)
            {
                // can't get to destination
                TrailGuide.Reset();
                return;
            }

            if (_currentNode.SpawnToken == _destinationNode.SpawnToken && _repathTries >= 1)
            {
                Arrive();
                return;
            }

            _isOnLineOfSightPath = false;
            if (!PathingManager.NeedsPath(Position, Destination, RoverSize, _allowUnwalkableDestination))
            {
                // no path required
                _isOnLineOfSightPath = true;
                return;
            }

            TrailGuide.RequestMovementPath(Position, Destination, RoverSize);
        }

        public virtual void SetMovementDirection() 
        {
            if (_isOnLineOfSightPath)
            {
                _targetDirection = Destination - Position;
            }
            else if (TrailGuide.HasPath)
                _targetDirection = TrailGuide.GetMovementDirection(Position, out _distanceToMove);
            else
            {
                Debug.Write("No vialable movement direction found, setting 0");
                _targetDirection = Vector3d.Zero;
                return;
            }

            // This is now the direction we want to be travelling in 
            _targetDirection.Normalize(out _distanceToMove);

            // Calculate steering and flocking forces for all agents
            if (IsInGroup)
                _targetDirection += NavigatorSteering.ComputeGroupSteering(Position, Speed);

            // Avoid any intersection agents!
            _targetDirection += NavigatorSteering.CalculateAvoidanceForce(this);
        }

        public virtual void SetMovementState()
        {
            Fixed64 currentSpeed = Velocity.Magnitude;
            Fixed64 stuckThreshold = _timescaledAcceleration / TrailblazerManager.FrameRate;
            Fixed64 slowDistance = _timescaledDeceleration > Fixed64.Zero ? currentSpeed / _timescaledDeceleration : Fixed64.Zero;

            Fixed64 moveAmount = FixedMath.Clamp01(_targetDirection.x.Abs() + _targetDirection.z.Abs());

            if (!TrailGuide.MovingToWaypoint && _distanceToMove < _closingDistance * StopMultiplier || !IsStuck && moveAmount == Fixed64.Zero)
            {
                Arrive();
                //TODO: Don't skip this frame of slowing down
                return;
            }
            else if (IsAbleToTurn)
                OnStartTurn?.Invoke(_targetDirection); //TODO: integrate this...

            if (_distanceToMove > slowDistance)
                _desiredVelocity = _targetDirection;
            else if (!TrailGuide.MovingToWaypoint && _distanceToMove <= slowDistance && _distanceToMove > _closingDistance * StopMultiplier)
            {
                Fixed64 closingSpeed = _distanceToMove / slowDistance;

                _desiredVelocity = _targetDirection * closingSpeed;
                _isDecelerating = true;
                // Reduce occurence of units preventing other units from reaching destination
                stuckThreshold *= 4;
            }

            CheckMovementStatus(stuckThreshold);

            // cap accelateration
            Fixed64 currentVelocity = _desiredVelocity.SqrMagnitude;
            if (currentVelocity > Acceleration)
                _desiredVelocity *= (Acceleration / FixedMath.Sqrt(currentVelocity)).CeilToInt();

            // Multiply our direction by speed for our desired speed
            _desiredVelocity *= Speed;

            // Cap speed as required
            if (currentSpeed > Speed)
                _desiredVelocity *= (Speed / currentSpeed).CeilToInt();

            // Apply the force
            Velocity += GetAdjustedVelocity(_desiredVelocity);
        }

        private void CheckMovementStatus(Fixed64 stuckThreshold)
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

                        if(TrailGuide.MovingToWaypoint)
                        if (IsInGroup)
                            MovementGroupID = -1;  // Attempt to repath agent by themselves

                        _isSearchingForPath = true;
                        _isOnLineOfSightPath = false;
                        TrailGuide.Reset();

                        _repathTries++;
                    }
                    else
                    {
                        Debug.WriteLine("Stuck Agent arriving!");
                        // we've tried to many times, we stuck stuck
                        IsStuck = true;
                        Arrive();
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
        
            if(_distanceToMove < _closingDistance && Vector3d.Dot(Position, _targetDirection) < Fixed64.Epsilon
                || _distanceToMove < _closingDistance * GlobalGridManager.NodeSize)
            {
                TrailGuide.CheckMovementStatus();
            }
        }

        // TODO: call this before setting velocity
        public Vector3d GetAdjustedVelocity(Vector3d desiredVelocity)
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

            OnArrive?.Invoke();
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

            _targetDirection = Vector3d.Zero;
            _desiredVelocity = Vector3d.Zero;

            _isSearchingForPath = false;
            _isOnLineOfSightPath = false;

            TrailGuide.Reset();

            OnStopMove?.Invoke();
        }

        public void PauseAutoStop()
        {
            _autoStopFrameCount = AutoPauseStopTime;
        }
    }
}