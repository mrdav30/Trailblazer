//=======================================================================
// NavigationOperationFrameChange.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

internal enum NavigationOperationFrameChangeKind
{
    MapCommit,
    MapRemove,
    Overlay
}

internal readonly struct NavigationOperationFrameChange
{
    private NavigationOperationFrameChange(
        NavigationOperationFrameChangeKind kind,
        string? mapId,
        PreparedNavigationMap? preparedMap,
        PreparedNavigationOverlay? preparedOverlay,
        OverlayReplacementPolicy replacementPolicy,
        long operationSequence)
    {
        Kind = kind;
        MapId = mapId;
        PreparedMap = preparedMap;
        PreparedOverlay = preparedOverlay;
        ReplacementPolicy = replacementPolicy;
        OperationSequence = operationSequence;
    }

    internal NavigationOperationFrameChangeKind Kind { get; }
    internal string? MapId { get; }
    internal PreparedNavigationMap? PreparedMap { get; }
    internal PreparedNavigationOverlay? PreparedOverlay { get; }
    internal OverlayReplacementPolicy ReplacementPolicy { get; }
    internal long OperationSequence { get; }

    internal static NavigationOperationFrameChange MapCommit(
        PreparedNavigationMap map,
        OverlayReplacementPolicy replacementPolicy,
        long operationSequence) => new(
        NavigationOperationFrameChangeKind.MapCommit,
        map.Map.MapId,
        map,
        preparedOverlay: null,
        replacementPolicy,
        operationSequence);

    internal static NavigationOperationFrameChange MapRemove(
        string mapId,
        long operationSequence) => new(
        NavigationOperationFrameChangeKind.MapRemove,
        mapId,
        preparedMap: null,
        preparedOverlay: null,
        OverlayReplacementPolicy.PreserveAndRevalidate,
        operationSequence);

    internal static NavigationOperationFrameChange Overlay(
        PreparedNavigationOverlay overlay,
        long operationSequence) => new(
        NavigationOperationFrameChangeKind.Overlay,
        mapId: null,
        preparedMap: null,
        overlay,
        OverlayReplacementPolicy.PreserveAndRevalidate,
        operationSequence);
}

internal delegate NavigationCandidatePublication NavigationCandidatePublisher(
    NavigationOperationCandidate candidate,
    int frame,
    NavigationOperationFrameChange[] changes,
    int changeCount);

internal delegate bool NavigationRetainedWorkGuard(long retainedBytes, int persistentPages);
