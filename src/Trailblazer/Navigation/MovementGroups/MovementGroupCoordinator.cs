using FixedMathSharp;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Navigation.MovementGroups;

/// <summary>
/// Tracks shared movement-group membership and resolves formation-preserving destinations for steering sessions.
/// </summary>
internal static class MovementGroupCoordinator
{
    private const int MinMovementGroupSize = 2;

    private const int MovementGroupHistoryFrames = 1;

    private static readonly SwiftDictionary<int, MovementGroupState> _movementGroups = new();

    private static readonly SwiftDictionary<Guid, MovementGroupMembership> _movementGroupMemberships = new();

    internal static void RegisterTrailblazerLifecycleHooks()
    {
        TrailblazerManager.RegisterOnResetCore(
            owner: "MovementGroupCoordinator.Reset",
            order: TrailblazerLifecycleOrder.NavigationReset,
            callback: Reset);
    }

    internal static void CacheOwner(MovementGroupSession session, Guid ownerId)
    {
        if (session.HasOwnerId && session.OwnerId == ownerId)
            return;

        if (session.HasOwnerId)
            _movementGroupMemberships.Remove(session.OwnerId);

        session.OwnerId = ownerId;
        session.HasOwnerId = true;
    }

    internal static void Prewarm(
        MovementGroupSession session,
        Guid ownerId,
        Vector3d requestedDestination,
        Vector3d position,
        Fixed64 radius)
    {
        if (session.GroupId < 0)
            return;

        CacheOwner(session, ownerId);
        UpdateTarget(session, requestedDestination, position, radius);
    }

    internal static MovementGroupTarget UpdateTarget(
        MovementGroupSession session,
        Vector3d requestedDestination,
        Vector3d position,
        Fixed64 radius,
        bool resetFormationOffset = false)
    {
        if (session.GroupId < 0)
            return new(MovementGroupTravelMode.None, requestedDestination);

        MovementGroupState group = GetOrCreateGroup(session.GroupId);
        MovementGroupMember self = GetOrCreateMember(session, group, ref resetFormationOffset);

        if (self.RequestedDestination != requestedDestination)
            resetFormationOffset = true;

        UpdateMemberState(self, requestedDestination, position, radius, resetFormationOffset);
        UpdateMembership(session, self, requestedDestination);

        int minFrame = TrailblazerManager.FrameCount - MovementGroupHistoryFrames;
        if (!TryGetFormationMetrics(group, requestedDestination, minFrame, out Vector3d groupCenter, out Fixed64 averageRadius, out int groupCount))
            return new(MovementGroupTravelMode.Individual, requestedDestination);

        Fixed64 maxSpreadSq = UpdateFormationOffsets(group, requestedDestination, minFrame, groupCenter);
        Fixed64 allowedSpreadSq = averageRadius * averageRadius * (Fixed64)(groupCount * 2);
        Fixed64 distanceToSharedDestinationSq = (requestedDestination - groupCenter).SqrMagnitude;
        if (maxSpreadSq > allowedSpreadSq || distanceToSharedDestinationSq <= maxSpreadSq)
            return new(MovementGroupTravelMode.GroupIndividual, requestedDestination);

        return new(MovementGroupTravelMode.Formation, requestedDestination + self.FormationOffset);
    }

    internal static void Remove(MovementGroupSession session)
    {
        if (session.GroupId < 0)
            return;

        if (_movementGroups.TryGetValue(session.GroupId, out MovementGroupState group))
        {
            if (group.Members.TryGetValue(session.GroupIndex, out MovementGroupMember member) && member.HasOccupantId)
                _movementGroupMemberships.Remove(member.OccupantId);

            group.Members.TryRemoveAt(session.GroupIndex);
            if (group.Members.Count == 0)
                _movementGroups.Remove(session.GroupId);
        }
        else if (session.HasOwnerId)
        {
            _movementGroupMemberships.Remove(session.OwnerId);
        }

        session.GroupIndex = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsNeighbor(
        MovementGroupSession session,
        Guid otherId,
        Vector3d requestedDestination,
        int currentFrame)
    {
        if (session.GroupId < 0 || !_movementGroupMemberships.TryGetValue(otherId, out MovementGroupMembership membership))
            return false;

        return membership.GroupId == session.GroupId
            && membership.RequestedDestination == requestedDestination
            && membership.LastSeenFrame >= currentFrame - MovementGroupHistoryFrames;
    }

    internal static void Reset()
    {
        _movementGroups.Clear();
        _movementGroupMemberships.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MovementGroupState GetOrCreateGroup(int groupId)
    {
        if (_movementGroups.TryGetValue(groupId, out MovementGroupState group))
            return group;

        group = new();
        _movementGroups[groupId] = group;
        return group;
    }

    private static MovementGroupMember GetOrCreateMember(
        MovementGroupSession session,
        MovementGroupState group,
        ref bool resetFormationOffset)
    {
        if (group.Members.TryGetValue(session.GroupIndex, out MovementGroupMember self))
            return self;

        self = new();
        session.GroupIndex = group.Members.Add(self);
        resetFormationOffset = true;
        return self;
    }

    private static void UpdateMemberState(
        MovementGroupMember self,
        Vector3d requestedDestination,
        Vector3d position,
        Fixed64 radius,
        bool resetFormationOffset)
    {
        self.Position = position;
        self.Radius = radius;
        self.RequestedDestination = requestedDestination;
        self.LastSeenFrame = TrailblazerManager.FrameCount;

        if (resetFormationOffset)
            self.HasFormationOffset = false;
    }

    private static void UpdateMembership(
        MovementGroupSession session,
        MovementGroupMember self,
        Vector3d requestedDestination)
    {
        if (!session.HasOwnerId)
            return;

        if (self.HasOccupantId && self.OccupantId != session.OwnerId)
            _movementGroupMemberships.Remove(self.OccupantId);

        self.OccupantId = session.OwnerId;
        self.HasOccupantId = true;

        _movementGroupMemberships[session.OwnerId] = new MovementGroupMembership
        {
            GroupId = session.GroupId,
            RequestedDestination = requestedDestination,
            LastSeenFrame = self.LastSeenFrame
        };
    }

    private static bool TryGetFormationMetrics(
        MovementGroupState group,
        Vector3d requestedDestination,
        int minFrame,
        out Vector3d groupCenter,
        out Fixed64 averageRadius,
        out int groupCount)
    {
        groupCenter = Vector3d.Zero;
        averageRadius = Fixed64.Zero;
        groupCount = 0;

        foreach (MovementGroupMember member in group.Members)
        {
            if (!IsEligibleGroupMember(member, requestedDestination, minFrame))
                continue;

            groupCenter += member.Position;
            averageRadius += member.Radius;
            groupCount++;
        }

        if (groupCount < MinMovementGroupSize)
            return false;

        groupCenter /= groupCount;
        averageRadius = (averageRadius / groupCount) + (TrailblazerWorldManager.VoxelSize * Fixed64.Half);
        return true;
    }

    private static Fixed64 UpdateFormationOffsets(
        MovementGroupState group,
        Vector3d requestedDestination,
        int minFrame,
        Vector3d groupCenter)
    {
        Fixed64 maxSpreadSq = Fixed64.Zero;

        foreach (MovementGroupMember member in group.Members)
        {
            if (!IsEligibleGroupMember(member, requestedDestination, minFrame))
                continue;

            Vector3d formationOffset = member.Position - groupCenter;
            if (!member.HasFormationOffset)
            {
                member.FormationOffset = formationOffset;
                member.HasFormationOffset = true;
            }

            Fixed64 spreadSq = formationOffset.SqrMagnitude;
            if (spreadSq > maxSpreadSq)
                maxSpreadSq = spreadSq;
        }

        return maxSpreadSq;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsEligibleGroupMember(MovementGroupMember member, Vector3d requestedDestination, int minFrame)
    {
        return member.LastSeenFrame >= minFrame
            && member.RequestedDestination == requestedDestination;
    }
}
