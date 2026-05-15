using FixedMathSharp;
using GridForge.Grids;
using System;
using System.Runtime.CompilerServices;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

public partial class NavSteering
{
    #region Steering Behaviors (Group & Avoidance)

    /// <summary>
    /// Computes a combined steering vector—
    /// Separation, Alignment, Cohesion, plus single-nearest obstacle avoidance.
    /// </summary>
    public Vector3d ComputeCombinedSteering(
        Vector3d position,
        Vector3d velocity,
        Fixed64 speed,
        Fixed64 radius,
        Guid id)
    {
        if (speed <= Fixed64.Zero)
            return Vector3d.Zero;

        TrailblazerWorldContext context = ResolveContext();
        int currentFrame = context.FrameCount;

        // we need to see everybody who might influence us—either for group or for avoidance
        Fixed64 groupRadius = radius * GroupFactor;
        Fixed64 invGR = Fixed64.One / groupRadius;
        Fixed64 avoidRadius = radius * AvoidFactor;
        Fixed64 scanRadius = FixedMath.Max(groupRadius, avoidRadius);
        Fixed64 groupRadiusSq = groupRadius * groupRadius;

        // Accumulators
        Vector3d separation = Vector3d.Zero;
        Vector3d alignment = Vector3d.Zero;
        Vector3d cohesionCM = Vector3d.Zero;
        int groupCount = 0;

        ISteer? closest = null;
        Fixed64 closestDistSq = avoidRadius * avoidRadius;

        GridScanManager.ScanRadiusInto<ISteer>(
            context.World,
            position,
            scanRadius,
            _nearbySteerAgents,
            _scanScratch);

        for (int i = 0; i < _nearbySteerAgents.Count; i++)
        {
            ISteer other = _nearbySteerAgents[i];
            if (other.Radius <= Fixed64.Zero)
                continue;

            if (other.GlobalId == id)
                continue;

            Vector3d offset = other.Position - position;
            Fixed64 distSq = offset.SqrMagnitude;
            if (distSq <= Fixed64.Epsilon)
                continue;

            // Group behaviors
            if (IsGroupNeighbor(other.GlobalId, currentFrame) && distSq < groupRadiusSq)
            {
                groupCount++;
                Fixed64 d = FixedMath.Sqrt(distSq);
                Fixed64 invD = Fixed64.One / d;
                Vector3d norm = offset * invD;  // offset.Normal
                // stronger separation the closer they are
                Fixed64 push = (groupRadius - d) * invGR;
                separation -= norm * push;
                alignment += other.Velocity.Normal;
                cohesionCM += other.Position;
            }

            // Track nearest for avoidance
            if (distSq < closestDistSq)
            {
                closestDistSq = distSq;
                closest = other;
            }
        }

        Vector3d groupForce = Vector3d.Zero;
        // Finalize group forces
        if (groupCount > 0)
        {
            Vector3d sep = separation * BehaviorWeights.Separation;
            Vector3d align = (alignment / groupCount).Normal * BehaviorWeights.Alignment;
            Vector3d coh = ((cohesionCM / groupCount - position).Normal) * BehaviorWeights.Cohesion;
            groupForce = sep + align + coh;
        }

        // Compute avoidance
        Vector3d avoidance = Vector3d.Zero;
        if (closest != null)
        {
            Vector3d dir = closest.Position - position;
            // pick left/right dodge
            bool dodgeLeft = Vector3d.Dot(velocity, dir) >= Fixed64.Zero;
            Vector3d perp = dodgeLeft
                ? new(-dir.z, Fixed64.Zero, dir.x)
                : new(dir.z, Fixed64.Zero, -dir.x);

            // prioritize evasive action when facing direct collision(dot ~ ±1),
            // and de-emphasize near misses(dot ~0)
            Fixed64 dynamicAvoidWeight = Vector3d.Dot(velocity.Normal, dir.Normal);
            Fixed64 totalAvoidWeight = BehaviorWeights.Avoidance * dynamicAvoidWeight;
            avoidance = perp.Normal
                * ((radius + closest.Radius) / FixedMath.Sqrt(closestDistSq))
                * totalAvoidWeight;
        }

        return groupForce + avoidance;
    }

    #endregion

    #region Movement Groups

    private void CacheOwner(ISteer vessel)
    {
        if (IsInGroup)
            MovementGroups.CacheOwner(_movementGroupSession, vessel.GlobalId);
    }

    private void UpdateMovementGroupState(Vector3d position, bool resetFormationOffset = false)
    {
        var target = new MovementGroupTarget(
            travelMode: IsInGroup ? MovementGroupTravelMode.Individual : MovementGroupTravelMode.None,
            destination: _requestedDestination);

        if (IsInGroup && _currentRequest != null)
        {
            target = MovementGroups.UpdateTarget(
                _movementGroupSession,
                _requestedDestination,
                position,
                _agentRadius,
                resetFormationOffset);
        }

        _destination = target.Destination;
        _movementGroupMode = target.TravelMode;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fixed64 GetActiveStopMultiplier() =>
        _movementGroupMode == MovementGroupTravelMode.GroupIndividual
            ? DefaultGroupIndividualStop
            : StopMultiplier;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsGroupNeighbor(Guid otherId, int currentFrame)
        => MovementGroups.IsNeighbor(_movementGroupSession, otherId, _requestedDestination, currentFrame);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool UsesVolumeGuidance() => _currentRequest is VolumePathRequest;

    private void PublishRouteTopology(
        bool hasResolvedTopology,
        bool usesGuideTopology,
        bool requestsClimbIntent,
        bool force = false)
    {
        if (!force
            && _currentRouteHasResolvedTopology == hasResolvedTopology
            && _currentRouteUsesGuideTopology == usesGuideTopology
            && _currentRouteRequestsClimbIntent == requestsClimbIntent)
        {
            return;
        }

        _currentRouteHasResolvedTopology = hasResolvedTopology;
        _currentRouteUsesGuideTopology = usesGuideTopology;
        _currentRouteRequestsClimbIntent = requestsClimbIntent;
        unchecked
        {
            _currentRouteTopologyVersion++;
        }
    }

    #endregion
}
