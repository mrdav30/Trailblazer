using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering
{
    /// <summary>
    /// Handles agent steering and path navigation logic by coordinating pathfinding, movement, 
    /// and group behaviors within a lockstep simulation. Supports both direct line-of-sight travel 
    /// and guided path traversal using IGuide implementations like AStar or FlowField.
    /// </summary>
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
        protected const int DefaultPathRecheckCooldown = 16;

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
        /// The final destination this agent is attempting to reach.
        /// </summary>
        public Vector3d Destination { get; protected set; }

        /// <inheritdoc cref="DefaultPathRecheckCooldown"/>
        public int PathRecheckCooldownFrames = DefaultPathRecheckCooldown;

        public Vector3d TargetDirection { get; protected set; }

        public Vector3d LastTargetDirection { get; protected set; }

        /// <summary>
        /// The pathfinding configuration used for the current movement request, including size, and type.
        /// </summary>
        private IPathRequest _currentRequest;

        /// <inheritdoc cref="_currentRequest"/>
        public IPathRequest CurrentRequest => _currentRequest;

        /// <summary>
        /// Current guide used to compute the desired path or flow.
        /// </summary>
        private IGuide _trailGuide;

        /// <inheritdoc cref="_trailGuide"/>
        public IGuide TrailGuide => _trailGuide;

        /// <summary>
        /// Whether the navigator is following a path or guide to the destination.
        /// </summary>
        public bool ShouldMove { get; protected set; }

        /// <summary>
        /// Whether the agent has become stuck and exhausted repathing attempts.
        /// </summary>
        public bool IsStuck { get; protected set; }

        /// <summary>
        /// True if the agent can reach the destination without requiring a path.
        /// </summary>
        public bool HasLineOfSightPath { get; protected set; }

        /// <summary>
        /// Current pathfinding search status.
        /// </summary>
        protected bool _shouldRequestPathThisFrame;

        protected int _pathCheckCooldown;

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
        public bool HasTrailGuide => !HasLineOfSightPath && _trailGuide != null;

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
        public NavSteeringEvents Events { get; protected set; } = new();

        #endregion

        #region Group Properties

        public int MovementGroupID { get; set; } = -1;

        public bool IsInGroup => MovementGroupID != -1;

        #endregion

        #region Public Interface

        // Needs to be called every frame from input system
        // For AI pathfinding, only needs to be called once until agent destination is reached
        public virtual void ApplyPathRequest(
            IPathRequest pathRequest,
            Vector3d? destination = null,
            int groupId = -1)
        {
            // assume the navigator is being controlled
            if (pathRequest == null || !pathRequest.HasValidEndpoints)
            {
#if DEBUG
                Debug.WriteLine("Path request endpoints are not valid.");
#endif
                Arrive();
                return;
            }

            HasLineOfSightPath = false;
            IsAtDestination = false;

            StoppedFrameCount = 0;
            IsStuck = false;
            _stuckFrameCount = 0;

            AddToMovementGroup(groupId);

            ShouldMove = true;
            // NOTE: destination can be an exact point within a voxel, not neccesarily the voxel position
            Destination = destination ?? pathRequest.EndNode.WorldPosition;

            // TODO: once _currentRequest is set here...it should not be mutable outside this class. do we need to clone?
            _currentRequest = pathRequest;

            _repathTries = 0;
            _shouldRequestPathThisFrame = true;

            Events.OnMoveRequestApplied?.Invoke();
        }

        /// <summary>
        /// Applies a short delay to prevent auto-stopping behavior for a few frames.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PauseAutoStop() => _autoStopFrameCount = AutoPauseStopTime;

        public void SetTrailGuide(IGuide guide)
        {
            _trailGuide = guide;
            _shouldRequestPathThisFrame = _trailGuide != null;
        }

        public void AddToMovementGroup(int groupId)
        {
            if (groupId < 0) return;

            if (MovementGroupID >= 0) LeaveMovementGroup();

            // TODO: integrate movement group logic
            MovementGroupID = groupId;
        }

        public void LeaveMovementGroup()
        {
            // TODO: integrate movement group logic
            MovementGroupID = -1;
        }

        #endregion

        #region Simulation Lifecycle

        /// <summary>
        /// Initializes the navigator by setting up its defaults, events, traversal state, and movement controller.
        /// </summary>
        public virtual void OnInitialize(INavigate navigator)
        {
            // Fatter objects can afford to land imprecisely
            _closingDistance = FixedMath.Round(navigator.UnitRadius + GlobalGridManager.VoxelSize);

            LeaveMovementGroup();

            StoppedFrameCount = 0;
            _autoStopFrameCount = 0;

            StopMultiplier = DefaultDirectStop;

            _shouldRequestPathThisFrame = false;
            HasLineOfSightPath = false;
            ShouldMove = false;

            IsStuck = false;
            _stuckFrameCount = 0;
            _repathTries = 0;

            IsAtDestination = false;

            _currentRequest = null;
            _trailGuide = null;
        }

        /// <summary>
        /// Called every simulation step to handle agent steering and movement logic.
        /// </summary>
        public virtual void OnSimulate(INavigate navigator)
        {
            if (!CanMove)
                return;

            if (ShouldMove && !IsAtDestination)
            {
                if (_currentRequest == null)
                {
                    Arrive();
                    return;
                }

                // check if agent has to pathfind, otherwise straight path to rely on destination
                if (CanPathfind)
                {
                    if (!ValidateMovementPath(navigator.Position))
                    {
#if DEBUG
                        Debug.WriteLine("Invalid path detected!");
#endif
                        Events.OnInvalidPath?.Invoke();
                        Arrive();
                        return;
                    }
                }

                if (_pathCheckCooldown <= 0)
                {
                    HasLineOfSightPath = IsDestinationInSight(
                        navigator.Position,
                        Destination,
                        _currentRequest.UnitSize,
                        _currentRequest.AllowUnwalkable);

                    _pathCheckCooldown = PathRecheckCooldownFrames;
                }

                LastTargetDirection = TargetDirection;
                TargetDirection = FindTargetDirection(navigator);

                // Check if we're close enough to stop moving
                Fixed64 moveAmount = FixedMath.Clamp01(TargetDirection.x.Abs() + TargetDirection.z.Abs());
                bool reachedTarget = _distanceToTarget < _closingDistance * StopMultiplier;
                bool noInput = moveAmount == Fixed64.Zero;
                if (!HasTrailGuide && (reachedTarget || (!IsStuck && noInput)))
                {
                    Arrive();
                    return;
                }

                if (!CheckStuckStatus(navigator.Position, navigator.Speed, navigator.StuckThresholdSpeed))
                {
#if DEBUG
                    Debug.WriteLine("Stuck agent arriving!");
#endif
                    Events.OnIsStuck?.Invoke();
                    Arrive();
                    return;
                }

                if (TargetDirection != Vector3d.Zero)
                {
                    if (_trailGuide is IWaypointGuide waypointGuide && ShouldAdvanceToNextWaypoint())
                        waypointGuide.AdvanceWaypoint();

                    // Pass the request to the NavMotor (even if we just arrived since we were close enough)
                    Events.OnStartTraversal?.Invoke(TargetDirection);
                }
            }
            else
            {
                // Pass on the idle movement to the motor
                Events.OnStartTraversal?.Invoke(Vector3d.Zero);

                if (navigator.Speed <= Fixed64.Epsilon)
                    StoppedFrameCount++;
            }

            _autoStopFrameCount--;
            _pathCheckCooldown--;
        }

        /// <summary>
        /// Periodically called to initiate a pathfinding query based on the current position and destination.
        /// Note: This will run once on the next `Simulate` call after calling `ApplyPathRequest`
        /// </summary>
        protected virtual bool ValidateMovementPath(Vector3d origin)
        {
            if (!_shouldRequestPathThisFrame)
                return true;
            _shouldRequestPathThisFrame = false;

            // If navigator moved (or got moved), update the steering request.
            // Note: this shouldn't have changed after calling ApplyRequest.
            if (!_currentRequest.TrySetOrigin(origin) | !_currentRequest.HasValidEndpoints)
            {
#if DEBUG
                Debug.WriteLine("Path request is using invalid endpoints!");
#endif
                return false;
            }

            if (_currentRequest.HasZeroDisplacement)
            {
                if (_repathTries >= 1) return false; // we're stuck and aren't moving
                // We have no where to go
                return true;
            }

            HasLineOfSightPath = IsDestinationInSight(
                origin,
                Destination,
                _currentRequest.UnitSize,
                _currentRequest.AllowUnwalkable);
            if (HasLineOfSightPath)
                return true;  // no path required

            _pathCheckCooldown = PathRecheckCooldownFrames;

            if (!_currentRequest.IsValid | !PathGuideFactory.RequestGuide(_currentRequest, out _trailGuide))
            {
#if DEBUG
                Debug.WriteLine($"Unable to retrieve a guide from {origin} to {Destination}");
#endif
                return false;
            }

            return true;
        }

        /// <summary>
        /// Computes the steering direction toward the destination or along the path.
        /// </summary>
        protected virtual Vector3d FindTargetDirection(INavigate body)
        {
            Vector3d targetDirection = Vector3d.Zero;
            if (HasLineOfSightPath)
                targetDirection = Destination - body.Position;
            else if (HasTrailGuide)
            {
                if (_trailGuide is IWaypointGuide waypointGuide)
                    targetDirection = waypointGuide.GetMovementDirection(body.Position);
                else
                    _trailGuide.TryGetMovementDirection(body.Position, out targetDirection);
            }

            if (targetDirection == Vector3d.Zero)
            {
#if DEBUG
                Debug.WriteLine("No vialable movement direction found.");
#endif
                return Vector3d.Zero;
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
        /// Determines whether the agent should advance to the next waypoint based on proximity and heading alignment.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ShouldAdvanceToNextWaypoint()
        {
            return _distanceToTarget < _closingDistance && Vector3d.Dot(TargetDirection, LastTargetDirection) < Fixed64.Epsilon
                || _distanceToTarget < _closingDistance * GlobalGridManager.VoxelSize;
        }

        /// <summary>
        /// Evaluates the agent's current movement direction and velocity, updating stuck and arrival state.
        /// </summary>
        protected virtual bool CheckStuckStatus(
            Vector3d position,
            Fixed64 speed,
            Fixed64 stuckThreshold)
        {
            if (!CanAutoStop)
                return true;

            // If unit has not moved stuckThreshold in a frame, it's stuck
            if (stuckThreshold > Fixed64.Zero && speed < stuckThreshold)
            {
                _stuckFrameCount++;

                if (_stuckFrameCount > StuckFrameThreshold)
                {
                    if (_repathTries < StuckRepathTries)
                    {
                        HasLineOfSightPath = false;

                        if (IsInGroup)
                            LeaveMovementGroup();  // Attempt to repath agent by themselves

                        if (HasTrailGuide && _trailGuide.TryGetFallbackDirection(position, out Vector3d fallback))
                        {
                            TargetDirection = fallback;
                            _repathTries++;
                            _stuckFrameCount = 0;
                            return true;
                        }

                        // Attempt to find another path on the next frame
                        TargetDirection = Vector3d.Zero;
                        _shouldRequestPathThisFrame = true;

                        // Reset the guide and have them try a new path (don't pool a bad path)
                        if (_trailGuide != null)
                        {
                            PathGuideFactory.ReturnGuide(_trailGuide, true);
                            _trailGuide = null;
                        }

                        _repathTries++;
                        return true;
                    }

                    // we've tried to many times, we stuck stuck
                    IsStuck = true;
                    // Reset the guide and have them try a new path (don't pool a bad path)
                    if (_trailGuide != null)
                    {
                        PathGuideFactory.ReturnGuide(_trailGuide, true);
                        _trailGuide = null;
                    }

                    return false;
                }

                // Keep trying
                return true;
            }

            // agent isn't stuck
            IsStuck = false;
            _stuckFrameCount = 0;
            _repathTries = 0;

            return true;
        }

        /// <summary>
        /// Triggers the arrival event and resets internal movement tracking.
        /// </summary>
        public void Arrive()
        {
            StopMove();

            LeaveMovementGroup();

            if (_trailGuide != null)
                PathGuideFactory.ReturnGuide(_trailGuide);

            _trailGuide = null;
            _currentRequest = null;
            _distanceToTarget = Fixed64.Zero;
            IsAtDestination = true;
            Destination = Vector3d.Zero;
            TargetDirection = Vector3d.Zero;

            Events.OnArrive?.Invoke();
        }

        /// <summary>
        /// Resets the movement and pathfinding logic, halting the agent.
        /// </summary>
        public virtual void StopMove()
        {
            if (!ShouldMove)
                return;

            _autoStopFrameCount = 0;
            _stuckFrameCount = 0;
            StoppedFrameCount = 0;

            ShouldMove = false;
            _shouldRequestPathThisFrame = false;
            HasLineOfSightPath = false;

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
            if (!PathManager.NeedsPath(position, destination, unitSize, allowUnwalkable))
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

            foreach (IVoxelOccupant entity in ScanManager.ScanRadius(from, paddingRadius))
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

            foreach (IVoxelOccupant entity in ScanManager.ScanRadius(body.Position, avoidRadius))
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
