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
        #region Constants & Defaults

        /// <summary>
        /// Default range to scan for other agents when calculating steering behaviors.
        /// </summary>
        protected static readonly Fixed64 DefaultSearchRange = Fixed64.One * 10;

        /// <summary>
        /// Default padding radius used to maintain space between nearby agents.
        /// </summary>
        protected static readonly Fixed64 DefaultAvoidPadding = Fixed64.One * 3;

        /// <summary>
        /// Default weights used for group-based steering calculations (separation, alignment, cohesion).
        /// </summary>
        protected static readonly GroupBehaviorWeights DefaultBehaviorWeights = new()
        {
            Separation = (Fixed64)2,
            Alignment = Fixed64.Half,
            Cohesion = (Fixed64)0.2f
        };

        /// <summary>
        /// Default multiplier used to determine proximity tolerance when stopping at a destination.
        /// </summary>
        public static readonly Fixed64 DefaultDirectStop = Fixed64.FromRaw(0x40000000L); // 0.25f;

        /// <summary>
        /// Number of frames between pathfinding LOS rechecks.
        /// </summary>
        protected const int PathRecheckCooldownFrames = 16;

        /// <summary>
        /// Number of frames an agent must be below the movement threshold to be considered stuck.
        /// </summary>
        protected static readonly int StuckFrameThreshold = TrailblazerManager.FrameRate / 4;

        /// <summary>
        /// Maximum number of repath attempts before declaring the agent fully stuck.
        /// </summary>
        protected const int StuckRepathTries = 4;

        /// <summary>
        /// Number of frames to wait before allowing auto-stop again.
        /// </summary>
        protected static readonly int AutoPauseStopTime = TrailblazerManager.FrameRate / 8;

        #endregion

        #region Runtime State - Pathfinding

        /// <summary>
        /// Disable if unit doesn't need to find path, i.e. flying
        /// </summary>
        public bool CanPathfind = true;

        /// <summary>
        /// Current guide used to compute the desired path or flow.
        /// </summary>
        public IGuide TrailGuide { get; protected set; }

        /// <summary>
        /// Target destination in world space for the current path.
        /// </summary>
        public Vector3d Destination { get; protected set; }

        /// <summary>
        /// Whether the navigator is following a path or guide to the destination.
        /// </summary>
        public bool IsFollowingTrail { get; protected set; }

        /// <summary>
        /// Whether the agent has become stuck and exhausted repathing attempts.
        /// </summary>
        public bool IsStuck { get; protected set; }

        /// <summary>
        /// True if the agent can reach the destination without requiring a path.
        /// </summary>
        public bool HasLineOfSightPath { get; protected set; }

        /// <summary>
        /// Whether the destination node is reachable.
        /// </summary>
        public bool HasViableDestination { get; protected set; }

        /// <summary>
        /// Current pathfinding search status.
        /// </summary>
        protected bool _shouldRequestPathThisFrame;

        /// <summary>
        /// Most recently evaluated grid node under the agent.
        /// </summary>
        protected Node _currentNode;

        /// <summary>
        /// Final grid node targeted as the destination.
        /// </summary>
        protected Node _destinationNode;

        /// <summary>
        /// Counter used to space out line-of-sight checks.
        /// </summary>
        protected int _pathRecheckCooldown;

        /// <summary>
        /// Optional override to allow reaching unwalkable destinations (useful for edge cases).
        /// </summary>
        protected bool _allowUnwalkableDestination;

        /// <summary>
        /// How far we move each update
        /// </summary>
        protected Fixed64 _distanceToTarget;

        /// <inheritdoc cref="_distanceToTarget"/>
        public Fixed64 DistanceToTarget => _distanceToTarget;

        /// <summary>
        /// How far away the agent stops from the target
        /// </summary>
        private Fixed64 _closingDistance;

        /// <summary>
        /// Indicates whether the agent is actively following a guide path with queued waypoints.
        /// </summary>
        public bool IsMovingToWaypoint => !HasLineOfSightPath && TrailGuide.HasWaypoints;

        /// <summary>
        /// Has this unit arrived at destination?
        /// </summary>
        public bool IsAtDestination { get; protected set; }

        #endregion

        #region Runtime State - Steering & Motion

        /// <summary>
        /// Whether this agent can currently move.
        /// </summary>
        public bool CanMove = true;

        /// <summary>
        /// Number of consecutive frames where movement failed and deceleration is occurring.
        /// </summary>
        public int StoppedFrameCount { get; protected set; }

        /// <summary>
        /// Internal cooldown before the agent can automatically stop again (used for bursty movement).
        /// </summary>
        protected int _autoStopFrameCount;

        /// <summary>
        /// Indicates whether the agent is currently eligible for automatic stopping logic.
        /// </summary>
        public bool CanAutoStop => _autoStopFrameCount <= 0;

        /// <summary>
        /// Number of attempts to repath after getting stuck.
        /// </summary>
        protected int _repathTries;

        /// <summary>
        /// Number of frames the agent has failed movement checks (used for stuck detection).
        /// </summary>
        protected int _stuckFrameCount;

        /// <summary>
        /// Multiplier used to determine how close the agent must be to its target before stopping.
        /// </summary>
        public Fixed64 StopMultiplier { get; set; } = DefaultDirectStop;

        #endregion

        #region Events

        /// <summary>
        /// Container for delegate events that fire on pathfinding state changes (start, stop, arrive).
        /// </summary>
        public NavSteeringEvents Events { get; protected set; }

        #endregion

        #region Group Properties

        public int MovementGroupID { get; set; } = -1;

        public bool IsInGroup => MovementGroupID != -1;

        #endregion

        #region Public Interface

        // Needs to be called every frame from input system
        // For AI pathfinding, only needs to be called once until agent destination is reached
        public virtual void ApplySteeringRequest(
            SteeringRequest request,
            bool allowUnwalkableEndNode = false,
            int groupId = -1)
        {
            // If direction was set, assume the navigator is being controlled
            if (request.TrailGuideRequest == TrailGuideParadigm.None)
            {
                Debug.WriteLine($"No trail guide requested.");
                return;
            }

            _allowUnwalkableDestination = allowUnwalkableEndNode;
            MovementGroupID = groupId;

            ProcessTrailGuideRequest(request);
        }

        /// <summary>
        /// Applies a short delay to prevent auto-stopping behavior for a few frames.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PauseAutoStop() => _autoStopFrameCount = AutoPauseStopTime;

        #endregion

        #region Simulation Lifecycle

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

            _shouldRequestPathThisFrame = false;
            HasLineOfSightPath = false;
            IsFollowingTrail = false;

            HasViableDestination = false;
            Destination = Vector3d.Zero;

            IsStuck = false;
            _stuckFrameCount = 0;
            _repathTries = 0;

            IsAtDestination = true;

            TrailGuide = null;

            Events = new();
        }

        /// <summary>
        /// Called every simulation step to handle agent steering and movement logic.
        /// </summary>
        public virtual void OnSimulate(INavigate body)
        {
            if (!CanMove)
                return;

            if (IsFollowingTrail)
            {
                if (!HasViableDestination)
                    return;

                // check if agent has to pathfind, otherwise straight path to rely on destination
                if (CanPathfind)
                    ValidateMovementPath(body.Position, body.UnitSize);

                if (_pathRecheckCooldown <= 0)
                {
                    HasLineOfSightPath = IsDestinationInSight(
                        body.Position,
                        Destination,
                        body.UnitSize,
                        _allowUnwalkableDestination);

                    _pathRecheckCooldown = PathRecheckCooldownFrames;
                }

                Vector3d direction = FindTargetDirection(body);
                if (direction != Vector3d.Zero)
                    UpdateMovementStatus(body.Position, direction, body.Speed, body.StuckThresholdSpeed);
            }
            else
            {
                // Pass on the idle movement to the motor
                Events.OnStartGuidedTraversal(Vector3d.Zero);

                if (body.Speed > Fixed64.Zero)
                    StoppedFrameCount++;
            }

            _autoStopFrameCount = _autoStopFrameCount--;
            _pathRecheckCooldown = _pathRecheckCooldown--;
        }

        /// <summary>
        /// Computes the steering direction toward the destination or along the path.
        /// </summary>
        protected virtual Vector3d FindTargetDirection(INavigate body)
        {
            Vector3d targetDirection = Vector3d.Zero;
            if (HasLineOfSightPath)
                targetDirection = Destination - body.Position;
            else if (TrailGuide != null && TrailGuide.HasPath)
                targetDirection = TrailGuide.GetMovementDirection(body.Position);
            else
            {
                Debug.WriteLine("No vialable movement direction found.");
                return targetDirection;
            }

            // This is now the direction we want to be travelling in 
            targetDirection.Normalize(out _distanceToTarget);

            // Calculate steering and flocking forces for all agents
            if (IsInGroup)
                targetDirection += ComputeGroupSteering(body.Position, body.Speed);

            // Avoid any intersection agents!
            targetDirection += CalculateAvoidanceForce(body);

            return targetDirection.Normal;
        }

        /// <summary>
        /// Periodically called to initiate a pathfinding query based on the current position and destination.
        /// </summary>
        protected virtual void ValidateMovementPath(Vector3d position, Fixed64 unitSize)
        {
            bool isCurrentPositionValid;
            // if size requires consideration, use old next-best-node system
            // also a catch in case GetEndNode returns null
            if (unitSize <= Fixed64.One)
            {
                isCurrentPositionValid = NodeFinder.GetStartNode(
                    position,
                    Destination,
                    out _currentNode);
            }
            else
            {
                isCurrentPositionValid = NodeFinder.GetClosestNodeForSize(
                    position,
                    Destination,
                    unitSize,
                    out _currentNode);
            }

            if (!isCurrentPositionValid)
            {
                Debug.WriteLine("Agent is on an invalid position!");
                return;
            }

            if (!_shouldRequestPathThisFrame)
                return;

            _shouldRequestPathThisFrame = false;
            if (_currentNode.SpawnToken == _destinationNode.SpawnToken && _repathTries >= 1)
            {
                Arrive();
                return;
            }

            HasLineOfSightPath = IsDestinationInSight(position, Destination, unitSize, _allowUnwalkableDestination);
            if (HasLineOfSightPath)
                return;  // no path required

            TrailGuide?.RequestMovementPath(position, Destination, unitSize);
            _pathRecheckCooldown = PathRecheckCooldownFrames;
        }

        /// <summary>
        /// Triggers logic to construct a trail guide and begin navigation.
        /// </summary>
        protected virtual void ProcessTrailGuideRequest(SteeringRequest request)
        {
            // TODO: maybe this is where we can check from a cache pool of trail guides that hold a hash key value of from/destination/roversize
            TrailGuide = TrailGuideFactory.RequestGuide(request.TrailGuideRequest);

            HasLineOfSightPath = false;

            IsFollowingTrail = true;
            IsAtDestination = false;
            StoppedFrameCount = 0;
            IsStuck = false;
            _stuckFrameCount = 0;

            HasViableDestination = false;
            Destination = Vector3d.Zero;

            // if size requires consideration, use old next-best-node system
            // also a catch in case GetEndNode returns null
            if (request.UnitSize <= Fixed64.One)
            {
                HasViableDestination = NodeFinder.GetEndNode(
                    request.From,
                    request.Destination,
                    out _destinationNode,
                    _allowUnwalkableDestination);
            }
            else
            {
                HasViableDestination = NodeFinder.GetClosestNodeForSize(
                    request.Destination,
                    request.From,
                    request.UnitSize,
                    out _destinationNode,
                    _allowUnwalkableDestination);
            }

            if (!HasViableDestination)
            {
                // no viable destination found
                Arrive();
                return;
            }

            _repathTries = 0;

            Destination = _destinationNode.WorldPosition;

            _shouldRequestPathThisFrame = true;

            Events.OnStartMove?.Invoke();
        }

        /// <summary>
        /// Evaluates the agent's current movement direction and velocity, updating stuck and arrival state.
        /// </summary>
        protected virtual void UpdateMovementStatus(
            Vector3d position,
            Vector3d direction,
            Fixed64 speed,
            Fixed64 stuckThreshold)
        {
            Fixed64 moveAmount = FixedMath.Clamp01(direction.x.Abs() + direction.z.Abs());
            bool reachedTarget = _distanceToTarget < _closingDistance * StopMultiplier;
            bool noInput = moveAmount == Fixed64.Zero;

            if (!IsMovingToWaypoint && (reachedTarget || (!IsStuck && noInput)))
            {
                Arrive();
                direction = Vector3d.Zero;
            }

            if (!IsAtDestination)
            {
                CheckStuckStatus(speed, stuckThreshold);

                if (IsMovingToWaypoint && ShouldAdvanceToNextWaypoint(position, direction))
                    TrailGuide.MoveToNextWaypoint();
            }

            // Pass the request to the NavMotor (even if we just arrived since we were close enough)
            Events.OnStartGuidedTraversal?.Invoke(direction);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual bool ShouldAdvanceToNextWaypoint(Vector3d position, Vector3d direction)
        {
            return _distanceToTarget < _closingDistance && Vector3d.Dot(position, direction) < Fixed64.Epsilon
                || _distanceToTarget < _closingDistance * GlobalGridManager.NodeSize;
        }

        /// <summary>
        /// Evaluates whether the agent is stuck based on recent movement patterns.
        /// </summary>
        protected virtual void CheckStuckStatus(Fixed64 speed, Fixed64 stuckThreshold)
        {
            if (!CanAutoStop)
                return;

            // If unit has not moved stuckThreshold in a frame, it's stuck
            if (stuckThreshold > Fixed64.Zero && speed < stuckThreshold)
            {
                _stuckFrameCount++;

                if (_stuckFrameCount > StuckFrameThreshold)
                {
                    if (_repathTries < StuckRepathTries)
                    {
                        Debug.WriteLine("Stuck Agent!");

                        HasLineOfSightPath = false;

                        if (IsInGroup)
                            MovementGroupID = -1;  // Attempt to repath agent by themselves

                        // If we have a path, try to move to the next waypoint
                        if (IsMovingToWaypoint)
                        {
                            TrailGuide.MoveToNextWaypoint();
                            _repathTries++;
                            _stuckFrameCount = 0;
                            return;
                        }
                        else
                        {
                            _shouldRequestPathThisFrame = true;

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
                _stuckFrameCount = 0;
                _repathTries = 0;
            }
        }

        /// <summary>
        /// Triggers the arrival event and resets internal movement tracking.
        /// </summary>
        public void Arrive()
        {
            StopMove();

            _distanceToTarget = Fixed64.Zero;
            IsAtDestination = true;

            Events.OnArrive?.Invoke();
        }

        /// <summary>
        /// Resets the movement and pathfinding logic, halting the agent.
        /// </summary>
        public virtual void StopMove()
        {
            if (!IsFollowingTrail)
                return;

            if (MovementGroupID != -1)
                MovementGroupID = -1;

            _autoStopFrameCount = 0;
            _stuckFrameCount = 0;
            StoppedFrameCount = 0;

            IsFollowingTrail = false;
            _shouldRequestPathThisFrame = false;
            HasLineOfSightPath = false;

            // TODO: return the trail guide?
            TrailGuide = null;

            Events.OnStopMove?.Invoke();
        }

        #endregion

        #region Line-of-Sight & Reachability

        /// <summary>
        /// Whether the destination is currently visible and reachable from the agent's position.
        /// </summary>
        public static bool IsDestinationInSight(Vector3d position, Vector3d destination, Fixed64 unitSize, bool allowUnwalkable)
        {
            bool result = false;
            if (!PathingManager.NeedsPath(position, destination, unitSize, allowUnwalkable))
                result = true;

            return result;
        }

        #endregion

        #region Steering Behaviors (Group & Avoidance)

        /// <summary>
        /// Calculates a movement vector that encourages group cohesion, alignment, and separation.
        /// </summary>
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
                if (entity is not INavigate other)
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

        /// <summary>
        /// Seeks toward a target position with speed-based magnitude adjustment.
        /// </summary>
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

        /// <summary>
        /// Calculates a lateral avoidance force based on nearby navigators' predicted collisions.
        /// </summary>
        public static Vector3d CalculateAvoidanceForce(
            INavigate body,
            Fixed64? range = null,
            Func<INavigate, bool> filter = null)
        {
            if (body.Speed <= Fixed64.Zero)
                return Vector3d.Zero;

            INavigate closest = null;
            Fixed64 avoidRadius = range ?? DefaultSearchRange;
            Fixed64 minAvoidanceDistance = avoidRadius;

            foreach (var entity in ScanManager.ScanRadius(body.Position, avoidRadius))
            {
                if (entity is not INavigate other)
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

            // prioritize evasive action when facing direct collision (dot ~ ±1),
            // and de-emphasize near misses (dot ~ 0).
            Fixed64 angelWeight = FixedMath.Abs(Vector3d.Dot(body.Velocity.Normal, avoidanceDir.Normal));
            Vector3d force = perp * (combinedRadius / minAvoidanceDistance) * angelWeight;
            return force;
        }

        #endregion
    }
}
