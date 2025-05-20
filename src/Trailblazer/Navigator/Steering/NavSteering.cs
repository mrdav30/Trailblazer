using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation
{
    public class NavSteering
    {
        private static readonly Fixed64 DefaultSearchRange = Fixed64.One * 10;

        private static readonly Fixed64 DefaultAvoidPadding = Fixed64.One * 3;

        private static readonly GroupBehaviorWeights DefaultBehaviorWeights = new()
        {
            Separation = (Fixed64)2,
            Alignment = Fixed64.Half,
            Cohesion = (Fixed64)0.2f
        };

        // Stop multipliers determine accuracy required for stopping on the destination
        public static readonly Fixed64 DefaultDirectStop = Fixed64.FromRaw(0x40000000L); // 0.25f;
        public static readonly Fixed64 DecelerationMultiplier = (Fixed64)10f;

        private static readonly int _stuckFrameThreshold = TrailblazerManager.FrameRate / 4;
        private static readonly int _stuckRepathTries = 4;

        private static readonly int AutoPauseStopTime = TrailblazerManager.FrameRate / 8;

        #region Serialized

        public NavSteeringEvents Events { get; protected set; }

        public bool IsAbleToMove = true;

        public bool IsAbleToTurn = true;

        /// <summary>
        /// Disable if unit doesn't need to find path, i.e. flying
        /// </summary>
        public bool IsAbleToPathfind = true;

        #endregion

        public bool IsControlled { get; set; }

        public IGuide TrailGuide { get; set; }

        public Vector3d Destination { get; set; }

        public Vector3d LastDestination { get; set; }

        protected Node _currentNode;

        protected Node _destinationNode;

        public bool IsMoving { get; private set; }

        public Vector3d AveragePosition { get; set; } // used to check if stuck

        public bool IsStuck { get; private set; }

        /// <summary>
        /// Has this unit arrived at destination?
        /// </summary>
        public bool IsAtDestination { get; private set; }

        public Fixed64 StopMultiplier { get; set; } = DefaultDirectStop;

        private bool _isSearchingForPath;

        private Fixed64 _timescaledAcceleration;

        private Fixed64 _timescaledDeceleration;

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
        public TraversalRequest FrameMoveRequest { get; protected set; }

        public bool IsFollowingWaypoint => TrailGuide != null && TrailGuide.HasPath && TrailGuide.HasWaypoints;

        /// <summary>
        /// Initializes the navigator by setting up its defaults, events, traversal state, and movement controller.
        /// </summary>
        public virtual void OnInitialize(INavigate navigator)
        {
            // Fatter objects can afford to land imprecisely
            _closingDistance = FixedMath.Round(navigator.UnitRadius);

            MovementGroupID = -1;

            StoppedFrameCount = 0;
            _autoStopFrameCount = 0;

            StopMultiplier = DefaultDirectStop;

            _isSearchingForPath = false;
            _isOnLineOfSightPath = false;
            IsMoving = false;

            _viableDestination = false;
            Destination = Vector3d.Zero;

            IsStuck = false;
            _stuckFrameCount = 0;
            _repathTries = 0;

            IsAtDestination = true;
            AveragePosition = navigator.Position;

            TrailGuide = null;
            FrameMoveRequest = TraversalRequest.Empty;

            Events = new();
        }

        // Needs to be called every frame from input system
        // For AI pathfinding, only needs to be called once until agent destination is reached
        public virtual void RequestMovement(
            TraversalRequest request,
            bool allowUnwalkableEndNode = false,
            int groupId = -1)
        {
            if (request == null)
                ThrowHelper.ThrowArgumentNullException(nameof(request));

            FrameMoveRequest = request;
            _allowUnwalkableDestination = allowUnwalkableEndNode;
            MovementGroupID = groupId;

            // If direction was set, assume the navigator is being controlled
            if (request.TrailGuideRequest == TrailGuideParadigm.None)
            {
                ProccessControlledRequest();
                return;
            }

            ProccessTrailGuideRequest();
        }

        // TODO: we probably shouldn't let player controlled characters move onto unwalkable nodes...
        public virtual void ProccessControlledRequest()
        {
            IsControlled = true;

            // TODO: release back to pool if exists?
            TrailGuide = null;

            IsMoving = true;
            IsAtDestination = false;
            IsStuck = false;

            StoppedFrameCount = 0;
            _stuckFrameCount = 0;

            Events.OnStartMove?.Invoke();
        }

        public virtual void ProccessTrailGuideRequest()
        {
            if (FrameMoveRequest.Destination == null)
            {
                Debug.WriteLine($"Missing required {nameof(FrameMoveRequest.Destination)} for TrailGuide.");
                return;
            }

            IsControlled = false;

            // TODO: maybe this is where we can check from a cache pool of trail guides that hold a hash key value of from/destination/roversize
            TrailGuide = TrailGuideFactory.RequestGuide(FrameMoveRequest.TrailGuideRequest);

            // We'll let the TrailGuide find the direction
            FrameMoveRequest.Direction = default;

            _isOnLineOfSightPath = false;

            IsMoving = true;
            IsAtDestination = false;
            StoppedFrameCount = 0;
            IsStuck = false;
            _stuckFrameCount = 0;

            _viableDestination = false;
            Destination = Vector3d.Zero;

            // if size requires consideration, use old next-best-node system
            // also a catch in case GetEndNode returns null
            if (FrameMoveRequest.UnitSize <= Fixed64.One)
            {
                _viableDestination = NodeFinder.GetEndNode(
                    FrameMoveRequest.CurrentPosition,
                    FrameMoveRequest.Destination.Value,
                    out _destinationNode,
                    _allowUnwalkableDestination);
            }
            else
            {
                _viableDestination = NodeFinder.GetClosestNodeForSize(
                    FrameMoveRequest.CurrentPosition,
                    FrameMoveRequest.Destination.Value,
                    FrameMoveRequest.UnitSize,
                    out _destinationNode,
                    _allowUnwalkableDestination);
            }

            if (!_viableDestination)
            {
                // no viable destination found
                Arrive();
                return;
            }

            _repathTries = 0;

            Destination = _destinationNode.WorldPosition;

            if (IsInGroup)
                _isSearchingForPath = true;
            else
                _isSearchingForPath = false;

            Events.OnStartMove?.Invoke();
        }

        // Used for AI that already have a path and want to jump
        public virtual void ToggleJumpStatus(bool status)
        {
            if (!IsControlled) return;
            FrameMoveRequest.IsRequestingJump = status;
        }

        // Used for AI that already have a path and want to change their rate of speed
        public virtual void SetTraversalSpeed(TrekRate rate)
        {
            if (!IsControlled) return;
            FrameMoveRequest.Rate = rate;
        }

        public virtual void OnSimulate(IAvoidanceBody body)
        {
            if (!IsAbleToMove)
                return;

            FrameMoveRequest.CurrentPosition = body.Position;
            FrameMoveRequest.CurrentRotation = body.Rotation;
            AveragePosition = Vector3d.Lerp(AveragePosition, body.Position, Fixed64.Half);

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
                    RequestPathFromTrailGuide(body.Position, body.UnitSize);

                FindTargetDirection(body);
                if (FrameMoveRequest.Direction != Vector3d.Zero)
                    FollowTrail(body.Position, body.Speed);
            }
            else
            {
                Events.OnStartTraversal(FrameMoveRequest);

                StoppedFrameCount = body.Speed > Fixed64.Zero ? 0 : StoppedFrameCount++;
            }

            _autoStopFrameCount--;
        }

        protected virtual void ProcessControlledMovement()
        {
            if (IsAbleToTurn)
                Events.OnStartTurn?.Invoke(FrameMoveRequest.Direction); //TODO: integrate this...

            // Pass the request to the NavMotor
            Events.OnStartTraversal?.Invoke(FrameMoveRequest);
            FrameMoveRequest = TraversalRequest.Empty;
        }

        protected virtual void RequestPathFromTrailGuide(Vector3d position, Fixed64 unitSize)
        {
            if (!_isSearchingForPath)
                return;

            bool isCurrentPositionValid = false;
            // if size requires consideration, use old next-best-node system
            // also a catch in case GetEndNode returns null
            if (FrameMoveRequest.UnitSize <= Fixed64.One)
            {
                isCurrentPositionValid = NodeFinder.GetStartNode(
                    position,
                    Destination,
                    out _destinationNode,
                    _allowUnwalkableDestination);
            }
            else
            {
                isCurrentPositionValid = NodeFinder.GetClosestNodeForSize(
                    position,
                    Destination,
                    FrameMoveRequest.UnitSize,
                    out _destinationNode,
                    _allowUnwalkableDestination);
            }

            if (!isCurrentPositionValid)
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
            if (!PathingManager.NeedsPath(position, Destination, unitSize, _allowUnwalkableDestination))
            {
                // no path required
                // TODO: releaes the trailguide back to a pool
                TrailGuide = null;
                _isOnLineOfSightPath = true;
                return;
            }

            TrailGuide?.RequestMovementPath(position, Destination, unitSize);
        }

        protected virtual void FindTargetDirection(IAvoidanceBody body)
        {
            if (_isOnLineOfSightPath)
                FrameMoveRequest.Direction = Destination - body.Position;
            else if (TrailGuide != null && TrailGuide.HasPath)
                FrameMoveRequest.Direction = TrailGuide.GetMovementDirection(body.Position);
            else
            {
                Debug.Write("No vialable movement direction found.");
                FrameMoveRequest.Direction = Vector3d.Zero;
                return;
            }

            // This is now the direction we want to be travelling in 
            FrameMoveRequest.Direction.Normalize(out _distanceToMove);

            // Calculate steering and flocking forces for all agents
            if (IsInGroup)
                FrameMoveRequest.Direction += ComputeGroupSteering(body.Position, body.Speed);

            // Avoid any intersection agents!
            FrameMoveRequest.Direction += CalculateAvoidanceForce(body);
        }

        protected virtual void FollowTrail(Vector3d position, Fixed64 speed)
        {
            Fixed64 stuckThreshold = _timescaledAcceleration > Fixed64.Zero ? _timescaledAcceleration / TrailblazerManager.FrameRate : Fixed64.Zero;
            Fixed64 slowDistance = _timescaledDeceleration > Fixed64.Zero ? _timescaledDeceleration / speed : Fixed64.Zero;

            Fixed64 moveAmount = FixedMath.Clamp01(FrameMoveRequest.Direction.x.Abs() + FrameMoveRequest.Direction.z.Abs());

            if (IsAbleToTurn)
                Events.OnStartTurn?.Invoke(FrameMoveRequest.Direction); //TODO: integrate this...

            if (!IsFollowingWaypoint)
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

                    FrameMoveRequest.Direction *= closingSpeed;
                    // Reduce occurence of units preventing other units from reaching destination
                    stuckThreshold *= 4;
                }
            }

            UpdateMovementStatus(position, stuckThreshold);

            // Pass the request to the NavMotor
            Events.OnStartTraversal?.Invoke(FrameMoveRequest);
            FrameMoveRequest = TraversalRequest.Empty;
        }

        protected virtual void UpdateMovementStatus(Vector3d position, Fixed64 stuckThreshold)
        {
            _stuckFrameCount++;

            if (!CanAutoStop)
                return;

            // If unit has not moved stuckThreshold in a frame, it's stuck
            if (Vector3d.SqrDistance(position, AveragePosition) <= (stuckThreshold * stuckThreshold))
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

            bool canMoveToNextWayPoint = _distanceToMove < _closingDistance && Vector3d.Dot(position, FrameMoveRequest.Direction) < Fixed64.Epsilon
                || _distanceToMove < _closingDistance * GlobalGridManager.NodeSize;
            if (canMoveToNextWayPoint)
                TrailGuide?.MoveToNextWaypoint();
        }

        /// <summary>
        /// Finalizes traversal by updating movement calculations and applying corrections.
        /// </summary>
        /// <remarks>
        /// Should be called after physics bodies apply velocity changes
        /// </remarks>
        public virtual void UpdateTimeScaledValues(Fixed64 speed, Vector3d acceleration)
        {
            if (speed == Fixed64.Zero)
            {
                _timescaledAcceleration = Fixed64.Zero;
                _timescaledDeceleration = Fixed64.Zero;
                return;
            }

            // Update time scaling
            _timescaledAcceleration = ((acceleration * speed) / TrailblazerManager.FrameRate).Magnitude;
            _timescaledDeceleration = _timescaledAcceleration > Fixed64.Zero
                ? _timescaledAcceleration * DecelerationMultiplier
                : Fixed64.Zero;
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
            StoppedFrameCount = 0;

            FrameMoveRequest = TraversalRequest.Empty;

            _isSearchingForPath = false;
            _isOnLineOfSightPath = false;

            // TODO: return the trail guide?
            TrailGuide = null;

            Events.OnStopMove?.Invoke();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PauseAutoStop() => _autoStopFrameCount = AutoPauseStopTime;

        public static Vector3d ComputeGroupSteering(
            Vector3d from,
            Fixed64 speed,
            Fixed64? padding = null,
            GroupBehaviorWeights? weights = null)
        {
            int neighboursCount = 0;
            Fixed64 paddingRadius = padding ?? DefaultAvoidPadding;
            GroupBehaviorWeights groupWeights = weights ?? DefaultBehaviorWeights;

            Vector3d totalForce = Vector3d.Zero;
            Vector3d averageHeading = Vector3d.Zero;
            //  Sum up the position of our neighbours
            Vector3d centerOfMass = Vector3d.Zero;

            foreach (INodeOccupant entity in ScanManager.ScanRadius(from, paddingRadius))
            {
                if (entity is not IAvoidanceBody other)
                    continue;

                Vector3d distance = from - entity.WorldPosition;
                distance.Normalize(out Fixed64 distanceMagnitude);

                // Move away from neighbor if we are too close to
                totalForce += (distance * (Fixed64.One - (distanceMagnitude / paddingRadius)));

                //  Move closer to entities we are near but not close enough to
                centerOfMass += entity.WorldPosition;

                //  Change our direction to be closer to our neighbours that are within the max distance and are moving
                if (other.Velocity != Vector3d.Zero)
                    averageHeading += other.Velocity.Normal;

                neighboursCount++;
            }

            if (neighboursCount <= 0)
                return Vector3d.Zero;

            //  Separation calculates a force to move away from all of our neighbours. 
            //  We do this by calculating a force from them to us and scaling it so the force is greater the closer they are.
            Vector3d seperation = totalForce * (speed / (neighboursCount * Fixed64.One));

            //  Cohesion and Alignment are for when other agents going to a similar location as us.
            //  Otherwise we’ll get caught up when other agents move past.

            Fixed64 invNeighborCount = Fixed64.One / neighboursCount;

            //  Alignment calculates a force so that our direction is closer to our neighbours.
            //  It does this similar to cohesion, but by summing up the direction vectors (normalised velocities) of ourself 
            //  and our neighbours and working out the average direction.
            //  Divide by amount of neighbors to get the average heading
            Vector3d alignment = averageHeading * invNeighborCount;

            //  Cohesion calculates a force that will bring us closer to our neighbours, so we move together as a group rather than individually.
            //  Cohesion calculates the average position of our neighbours and ourself, and steers us towards it
            //  seek this position
            Vector3d cohesion = SteeringBehaviorSeek(from, centerOfMass * invNeighborCount, speed);

            //  Combine them to come up with a total force to apply, decreasing the effect of cohesion
            return (seperation * groupWeights.Separation) + (alignment * groupWeights.Alignment) + (cohesion * groupWeights.Cohesion);
        }

        private static Vector3d SteeringBehaviorSeek(Vector3d from, Vector3d destination, Fixed64 speed)
        {
            if (destination == from)
                return Vector3d.Zero;

            // Desired change of location
            Vector3d desired = destination - from;

            desired.Normalize(out Fixed64 desiredSpeed);
            //Desired velocity (move there at maximum speed)
            return desiredSpeed > Fixed64.Zero ? desired * (speed / desiredSpeed) : Vector3d.Zero;
        }

        public static Vector3d CalculateAvoidanceForce(
            IAvoidanceBody body,
            Fixed64? range = null,
            Func<IAvoidanceBody, bool> filter = null)
        {
            if (body.Speed <= Fixed64.Zero)
                return Vector3d.Zero;

            IAvoidanceBody closest = null;
            Fixed64 avoidRadius = range ?? DefaultSearchRange;
            Fixed64 minAvoidanceDistance = avoidRadius;

            foreach (var entity in ScanManager.ScanRadius(body.Position, avoidRadius))
            {
                if (entity is not IAvoidanceBody other)
                    continue;

                if (filter != null && !filter(other))
                    continue;

                Vector3d toOther = other.Position - body.Position;
                toOther.Normalize(out Fixed64 distance);

                if (distance < minAvoidanceDistance)
                {
                    closest = other;
                    minAvoidanceDistance = distance;
                }
            }

            if (closest == null)
                return Vector3d.Zero;

            // Direction from agent to the other
            Vector3d avoidanceDir = closest.Position - body.Position;

            if (closest.IsAvoidingLeft)
                body.IsAvoidingLeft = true;
            else
            {
                // Left/right test using 2D determinant
                Fixed64 dot = body.Velocity.x * -avoidanceDir.z + body.Velocity.z * avoidanceDir.x;
                body.IsAvoidingLeft = dot > Fixed64.Zero;
            }

            // Rotate vector by ±90° in XZ
            Vector3d perp = body.IsAvoidingLeft
                ? new Vector3d(-avoidanceDir.z, Fixed64.Zero, avoidanceDir.x)
                : new Vector3d(avoidanceDir.z, Fixed64.Zero, -avoidanceDir.x);

            perp.Normalize();

            // Adjust force based on combined radius
            Fixed64 combinedRadius = body.UnitRadius + closest.UnitRadius;

            Vector3d force = perp * (combinedRadius / minAvoidanceDistance);
            return force;
        }
    }
}
