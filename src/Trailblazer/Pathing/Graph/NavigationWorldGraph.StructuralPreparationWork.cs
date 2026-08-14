//=======================================================================
// NavigationWorldGraph.StructuralPreparationWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

internal sealed partial class NavigationWorldGraph
{
    /// <summary>Prepares changed instance and lookup roots without replaying payloads at publication.</summary>
    internal sealed class StructuralPreparationWork
    {
        private readonly NavigationWorldGraph _source;
        private readonly NavigationOperationCandidate _candidate;
        private readonly NavigationOperationFrameChange[] _changes;
        private readonly int _changeCount;
        private readonly long _version;
        private NavigationInstanceDirectory _directory;
        private PersistentGridConfigurationMap<string> _mapIndex;
        private NavigationMapInstance.ComposeWork? _compose;
        private NavigationMapInstance? _prior;
        private NavigationMapInstance? _next;
        private string? _mapId;
        private int _changeIndex;
        private int _deltaIndex;
        private bool _directoryConsumed;
        private bool _indexConsumed;
        private long _retainedBytes;
        private int _persistentPages;
        private long _copiedPersistentBytes;
        private int _copiedPersistentPages;

        internal StructuralPreparationWork(
            NavigationWorldGraph source,
            NavigationOperationCandidate candidate,
            NavigationOperationFrameChange[] changes,
            int changeCount,
            long version)
        {
            _source = source;
            _candidate = candidate;
            _changes = changes;
            _changeCount = changeCount;
            _version = version;
            _directory = source._instances;
            _mapIndex = source._mapIndex;
            _retainedBytes = source.RetainedBytes;
            _persistentPages = source.PersistentPageCount;
        }

        internal bool IsComplete => Result != null;

        internal NavigationWorldGraph Result { get; private set; } = null!;

        internal long RetainedBytes => checked(
            128L
            + Math.Max(0L, _retainedBytes - _source.RetainedBytes)
            + _copiedPersistentBytes
            + GetComposeAdditionalRetainedBytes());

        internal int PersistentPageCount => checked(
            1
            + Math.Max(0, _persistentPages - _source.PersistentPageCount)
            + _copiedPersistentPages
            + GetComposeAdditionalPersistentPages());

        private long GetComposeAdditionalRetainedBytes()
        {
            if (_compose == null)
                return 0;
            long priorBytes = _prior?.RetainedBytes ?? 0;
            return Math.Max(0L, _compose.RetainedBytes - priorBytes);
        }

        private int GetComposeAdditionalPersistentPages()
        {
            if (_compose == null)
                return 0;
            int priorPages = _prior?.PersistentPageCount ?? 0;
            return Math.Max(0, _compose.PersistentPageCount - priorPages);
        }

        internal bool Advance(MaintenanceWorkMeter meter)
        {
            while (_changeIndex < _changeCount)
            {
                NavigationOperationFrameChange change = _changes[_changeIndex];
                if (change.Kind == NavigationOperationFrameChangeKind.Overlay)
                {
                    ReadOnlySpan<NavigationMapOverlayDelta> deltas =
                        change.PreparedOverlay!.Transaction.MapSpan;
                    while (_deltaIndex < deltas.Length)
                    {
                        if (!PrepareOverlay(deltas[_deltaIndex], meter))
                            return false;
                        _deltaIndex++;
                    }
                    _deltaIndex = 0;
                    _changeIndex++;
                    continue;
                }
                if (change.Kind == NavigationOperationFrameChangeKind.MapRemove)
                {
                    if (!PrepareRemoval(change.MapId!, meter))
                        return false;
                }
                else if (!PrepareCommit(change, meter))
                    return false;
                _changeIndex++;
            }
            _retainedBytes = checked(
                _retainedBytes
                - _source._explicitConnections.RetainedBytes
                + _candidate.ExplicitConnections.RetainedBytes);
            _persistentPages += _candidate.ExplicitConnections.PersistentPageCount
                - _source._explicitConnections.PersistentPageCount;
            Result = new NavigationWorldGraph(
                _version,
                _directory,
                _source.AreaCatalog,
                _mapIndex,
                _source.Composition,
                _candidate.ExplicitConnections,
                _source._closedStructuralComponents,
                _retainedBytes,
                _persistentPages);
            return true;
        }

        private bool PrepareCommit(
            NavigationOperationFrameChange change,
            MaintenanceWorkMeter meter)
        {
            if (_compose == null && _next == null)
            {
                _mapId = change.MapId!;
                _directory.TryGet(_mapId, out _prior!);
                PreparedNavigationMap prepared = change.PreparedMap!;
                NavigationMapOverlayState overlay = _prior != null
                    && change.ReplacementPolicy == OverlayReplacementPolicy.PreserveAndRevalidate
                        ? _prior.Overlay
                        : NavigationMapOverlayState.Empty;
                PersistentVoxelIndexMap<byte> dynamicAddresses = _prior != null
                    && change.ReplacementPolicy == OverlayReplacementPolicy.PreserveAndRevalidate
                        ? _prior.DynamicAddresses
                        : PersistentVoxelIndexMap<byte>.Empty;
                var state = new NavigationOperationCandidate.MapState(
                    prepared.Map,
                    prepared.BakeVersion,
                    prepared.RetainedBytes,
                    overlay,
                    _prior != null
                        && change.ReplacementPolicy == OverlayReplacementPolicy.PreserveAndRevalidate
                            ? _prior.DynamicSlotGeneration
                            : _prior != null ? checked(_prior.DynamicSlotGeneration + 1) : 0,
                    dynamicAddresses,
                    prepared.BakedCellLookup);
                _compose = new NavigationMapInstance.ComposeWork(state, _prior, _version);
            }
            return AdvanceComposeAndAttach(meter);
        }

        private bool PrepareOverlay(
            NavigationMapOverlayDelta delta,
            MaintenanceWorkMeter meter)
        {
            if (_compose == null && _next == null)
            {
                _mapId = delta.MapId;
                if (!_directory.TryGet(delta.MapId, out _prior!))
                    return true;
                NavigationOperationCandidate.MapState state;
                if (!_candidate.TryGetState(delta.MapId, out state!) || state == null)
                {
                    state = new NavigationOperationCandidate.MapState(
                        _prior.Map,
                        _prior.BakeVersion,
                        _prior.PreparedMapRetainedBytes,
                        _prior.Overlay,
                        _prior.DynamicSlotGeneration,
                        _prior.DynamicAddresses,
                        _prior.BakedCellLookup);
                }
                _compose = new NavigationMapInstance.ComposeWork(
                    state,
                    _prior,
                    delta,
                    _version);
            }
            return AdvanceComposeAndAttach(meter);
        }

        private bool AdvanceComposeAndAttach(MaintenanceWorkMeter meter)
        {
            if (_next == null)
            {
                if (!_compose!.Advance(meter))
                    return false;
                _next = _compose.Result;
            }
            if (!_directoryConsumed)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                NavigationInstanceDirectory nextDirectory = _directory.Set(
                    _mapId!,
                    _next,
                    out int copiedNodes);
                RecordPersistentCopies(copiedNodes, 64L);
                _retainedBytes = checked(
                    _retainedBytes
                    - _directory.RetainedBytes
                    + nextDirectory.RetainedBytes
                    + _next.RetainedBytes
                    - (_prior?.RetainedBytes ?? 0));
                _persistentPages += nextDirectory.PersistentPageCount
                    - _directory.PersistentPageCount
                    + _next.PersistentPageCount
                    - (_prior?.PersistentPageCount ?? 0);
                _directory = nextDirectory;
                _directoryConsumed = true;
            }
            if (!_indexConsumed)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                PersistentGridConfigurationMap<string> before = _mapIndex;
                if (_prior != null
                    && !_prior.Map.GridBinding.Key.Equals(_next.Map.GridBinding.Key))
                {
                    _mapIndex = _mapIndex.Remove(
                        _prior.Map.GridBinding.Key,
                        out _,
                        out int removedCopies);
                    RecordPersistentCopies(removedCopies, 144L);
                }
                _mapIndex = _mapIndex.Set(
                    _next.Map.GridBinding.Key,
                    _next.MapId,
                    out int setCopies);
                RecordPersistentCopies(setCopies, 144L);
                _retainedBytes = checked(
                    _retainedBytes - before.RetainedBytes + _mapIndex.RetainedBytes);
                _persistentPages += _mapIndex.Count - before.Count;
                _indexConsumed = true;
            }
            ResetItem();
            return true;
        }

        private bool PrepareRemoval(string mapId, MaintenanceWorkMeter meter)
        {
            if (_mapId == null)
            {
                _mapId = mapId;
                if (!_directory.TryGet(mapId, out _prior!))
                {
                    ResetItem();
                    return true;
                }
            }
            if (!_directoryConsumed)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                NavigationInstanceDirectory nextDirectory =
                    _directory.Remove(mapId, out bool removed, out int copiedNodes);
                if (removed)
                {
                    RecordPersistentCopies(copiedNodes, 64L);
                    _retainedBytes = checked(
                        _retainedBytes
                        - _directory.RetainedBytes
                        + nextDirectory.RetainedBytes
                        - _prior!.RetainedBytes);
                    _persistentPages += nextDirectory.PersistentPageCount
                        - _directory.PersistentPageCount
                        - _prior.PersistentPageCount;
                    _directory = nextDirectory;
                }
                _directoryConsumed = true;
            }
            if (!_indexConsumed)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                PersistentGridConfigurationMap<string> before = _mapIndex;
                _mapIndex = _mapIndex.Remove(
                    _prior!.Map.GridBinding.Key,
                    out _,
                    out int copiedNodes);
                RecordPersistentCopies(copiedNodes, 144L);
                _retainedBytes = checked(
                    _retainedBytes - before.RetainedBytes + _mapIndex.RetainedBytes);
                _persistentPages += _mapIndex.Count - before.Count;
                _indexConsumed = true;
            }
            ResetItem();
            return true;
        }

        private void ResetItem()
        {
            _compose = null;
            _prior = null;
            _next = null;
            _mapId = null;
            _directoryConsumed = false;
            _indexConsumed = false;
        }

        private void RecordPersistentCopies(int copiedNodes, long bytesPerNode)
        {
            _copiedPersistentPages = checked(_copiedPersistentPages + copiedNodes);
            _copiedPersistentBytes = checked(
                _copiedPersistentBytes + (copiedNodes * bytesPerNode));
        }
    }
}
