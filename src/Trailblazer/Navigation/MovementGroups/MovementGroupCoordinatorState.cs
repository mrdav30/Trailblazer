//=======================================================================
// MovementGroupCoordinatorState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using SwiftCollections;

namespace Trailblazer.Navigation.MovementGroups;

/// <summary>
/// Tracks movement-group membership and resolves formation-preserving destinations for one world context.
/// </summary>
internal sealed class MovementGroupCoordinatorState
{
    private const int MinMovementGroupSize = 2;

    private const int MovementGroupHistoryFrames = 1;

    private readonly TrailblazerWorldContext _context;

    private readonly SwiftDictionary<int, MovementGroupState> _movementGroups = new();

    private readonly SwiftDictionary<Guid, MovementGroupMembership> _movementGroupMemberships = new();

    internal MovementGroupCoordinatorState(TrailblazerWorldContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    private int FrameCount => _context.FrameCount;

    private Fixed64 VoxelSize => _context.VoxelSize;

    internal void CacheOwner(MovementGroupSession session, Guid ownerId)
    {
        if (session.HasOwnerId && session.OwnerId == ownerId)
            return;

        if (session.HasOwnerId)
            _movementGroupMemberships.Remove(session.OwnerId);

        session.OwnerId = ownerId;
        session.HasOwnerId = true;
    }

    internal void Prewarm(
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

    internal MovementGroupTarget UpdateTarget(
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

        int minFrame = FrameCount - MovementGroupHistoryFrames;
        if (!TryGetFormationMetrics(group, requestedDestination, minFrame, out Vector3d groupCenter, out Fixed64 averageRadius, out int groupCount))
            return new(MovementGroupTravelMode.Individual, requestedDestination);

        Fixed64 maxSpreadSq = UpdateFormationOffsets(group, requestedDestination, minFrame, groupCenter);
        Fixed64 allowedSpreadSq = averageRadius * averageRadius * (Fixed64)(groupCount * 2);
        Fixed64 distanceToSharedDestinationSq = (requestedDestination - groupCenter).MagnitudeSquared;
        if (maxSpreadSq > allowedSpreadSq || distanceToSharedDestinationSq <= maxSpreadSq)
            return new(MovementGroupTravelMode.GroupIndividual, requestedDestination);

        return new(MovementGroupTravelMode.Formation, requestedDestination + self.FormationOffset);
    }

    internal void Remove(MovementGroupSession session)
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
    internal bool IsNeighbor(
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

    internal void Reset()
    {
        _movementGroups.Clear();
        _movementGroupMemberships.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MovementGroupState GetOrCreateGroup(int groupId)
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

    private void UpdateMemberState(
        MovementGroupMember self,
        Vector3d requestedDestination,
        Vector3d position,
        Fixed64 radius,
        bool resetFormationOffset)
    {
        self.Position = position;
        self.Radius = radius;
        self.RequestedDestination = requestedDestination;
        self.LastSeenFrame = FrameCount;

        if (resetFormationOffset)
            self.HasFormationOffset = false;
    }

    private void UpdateMembership(
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

    private bool TryGetFormationMetrics(
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
        averageRadius = (averageRadius / groupCount) + (VoxelSize * Fixed64.Half);
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

            Fixed64 spreadSq = formationOffset.MagnitudeSquared;
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
