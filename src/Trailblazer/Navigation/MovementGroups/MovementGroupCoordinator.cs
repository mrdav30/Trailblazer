using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Diagnostics.CodeAnalysis;
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

    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:Do not use 'ModuleInitializer'", Justification = "Movement-group reset hooks must self-register before TrailblazerManager methods run.")]
    internal static void RegisterTrailblazerLifecycleHooks()
    {
        TrailblazerManager.RegisterOnReset(
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

    internal static MovementGroupTarget UpdateTarget(
        MovementGroupSession session,
        Vector3d requestedDestination,
        Vector3d position,
        Fixed64 radius,
        bool resetFormationOffset = false)
    {
        if (session.GroupId < 0)
            return new(MovementGroupTravelMode.None, requestedDestination);

        if (!_movementGroups.TryGetValue(session.GroupId, out MovementGroupState group))
        {
            group = new();
            _movementGroups[session.GroupId] = group;
        }

        if (!group.Members.TryGetValue(session.GroupIndex, out MovementGroupMember self))
        {
            self = new();
            session.GroupIndex = group.Members.Add(self);
            resetFormationOffset = true;
        }

        if (self.RequestedDestination != requestedDestination)
            resetFormationOffset = true;

        self.Position = position;
        self.Radius = radius;
        self.RequestedDestination = requestedDestination;
        self.LastSeenFrame = TrailblazerManager.FrameCount;

        if (resetFormationOffset)
            self.HasFormationOffset = false;

        if (session.HasOwnerId)
        {
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

        Vector3d groupCenter = Vector3d.Zero;
        Fixed64 averageRadius = Fixed64.Zero;
        int groupCount = 0;
        int minFrame = TrailblazerManager.FrameCount - MovementGroupHistoryFrames;

        foreach (MovementGroupMember member in group.Members)
        {
            if (member.LastSeenFrame < minFrame || member.RequestedDestination != requestedDestination)
                continue;

            groupCenter += member.Position;
            averageRadius += member.Radius;
            groupCount++;
        }

        if (groupCount < MinMovementGroupSize)
            return new(MovementGroupTravelMode.Individual, requestedDestination);

        groupCenter /= groupCount;
        averageRadius = (averageRadius / groupCount) + (GlobalGridManager.VoxelSize * Fixed64.Half);

        Fixed64 maxSpreadSq = Fixed64.Zero;
        foreach (MovementGroupMember member in group.Members)
        {
            if (member.LastSeenFrame < minFrame || member.RequestedDestination != requestedDestination)
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
}
