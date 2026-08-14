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
        private readonly PersistentStringMap<bool> _changedMapIds;
        private readonly long _version;
        private NavigationInstanceDirectory _directory;
        private PersistentGridConfigurationMap<string> _mapIndex;
        private NavigationMapInstance.ComposeWork? _compose;
        private NavigationMapInstance? _prior;
        private NavigationMapInstance? _next;
        private string? _mapId;
        private int _changeIndex;
        private bool _directoryConsumed;
        private bool _indexConsumed;
        private long _retainedBytes;
        private int _persistentPages;
        private long _copiedPersistentBytes;
        private int _copiedPersistentPages;
        private long _ownedInstanceExclusiveBytes;
        private int _ownedInstanceExclusivePages;
        private bool _composeOwnershipTransferred;

        internal StructuralPreparationWork(
            NavigationWorldGraph source,
            NavigationOperationCandidate candidate,
            NavigationOperationFrameChange[] changes,
            int changeCount,
            PersistentStringMap<bool> changedMapIds,
            long version)
        {
            _source = source;
            _candidate = candidate;
            _changes = changes;
            _changeCount = changeCount;
            _changedMapIds = changedMapIds;
            _version = version;
            _directory = source._instances;
            _mapIndex = source._mapIndex;
            _retainedBytes = source.RetainedBytes;
            _persistentPages = source.PersistentPageCount;
        }

        internal bool IsComplete => Result != null;

        internal NavigationWorldGraph Result { get; private set; } = null!;

        internal bool CompositionChanged { get; private set; }

        internal long RetainedBytes => checked(
            128L
            + _ownedInstanceExclusiveBytes
            + _copiedPersistentBytes
            + GetComposeAdditionalRetainedBytes());

        internal int PersistentPageCount => checked(
            1
            + _ownedInstanceExclusivePages
            + _copiedPersistentPages
            + GetComposeAdditionalPersistentPages());

        private long GetComposeAdditionalRetainedBytes()
        {
            if (_compose == null)
                return 0;
            return checked(
                96L
                + (_composeOwnershipTransferred
                    ? 0L
                    : _compose.AdditionalExclusiveRetainedBytes));
        }

        private int GetComposeAdditionalPersistentPages()
        {
            if (_compose == null)
                return 0;
            return checked(
                1
                + (_composeOwnershipTransferred
                    ? 0
                    : _compose.AdditionalExclusivePersistentPages));
        }

        internal bool Advance(MaintenanceWorkMeter meter)
        {
            while (_changeIndex < _changedMapIds.Count)
            {
                string mapId = _changedMapIds.GetKeyAt(_changeIndex);
                if (_candidate.TryGetState(
                        mapId,
                        out NavigationOperationCandidate.MapState? state)
                    && state != null)
                {
                    if (!PrepareState(mapId, state, meter))
                        return false;
                }
                else
                {
                    if (!PrepareRemoval(mapId, meter))
                        return false;
                }
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
                _source._automaticSeams,
                _source._closedStructuralComponents,
                _source._allStructuralComponentsClosed,
                _retainedBytes,
                _persistentPages);
            return true;
        }

        private bool PrepareState(
            string mapId,
            NavigationOperationCandidate.MapState state,
            MaintenanceWorkMeter meter)
        {
            if (_compose == null && _next == null)
            {
                _mapId = mapId;
                _directory.TryGet(_mapId, out _prior!);
                if (_prior != null
                    && ReferenceEquals(_prior.Map, state.Map)
                    && _prior.BakeVersion == state.BakeVersion
                    && ReferenceEquals(_prior.Overlay, state.Overlay)
                    && _prior.DynamicSlotGeneration == state.DynamicSlotGeneration
                    && ReferenceEquals(_prior.DynamicAddresses, state.DynamicAddresses)
                    && ReferenceEquals(_prior.BakedCellLookup, state.BakedCellLookup))
                {
                    ResetItem();
                    return true;
                }
                bool overlayOnly = _prior != null
                    && ReferenceEquals(_prior.Map, state.Map)
                    && _prior.BakeVersion == state.BakeVersion
                    && _prior.DynamicSlotGeneration == state.DynamicSlotGeneration;
                _compose = overlayOnly
                    ? new NavigationMapInstance.ComposeWork(
                        state,
                        _prior!,
                        _changes,
                        _changeCount,
                        mapId,
                        _version)
                    : new NavigationMapInstance.ComposeWork(state, _prior, _version);
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
                CompositionChanged |= HasTopologyCompositionChange(_prior, _next);
            }
            if (!_directoryConsumed)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                _ownedInstanceExclusiveBytes = checked(
                    _ownedInstanceExclusiveBytes + _compose!.AdditionalExclusiveRetainedBytes);
                _ownedInstanceExclusivePages = checked(
                    _ownedInstanceExclusivePages + _compose.AdditionalExclusivePersistentPages);
                _composeOwnershipTransferred = true;
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
                    CompositionChanged = true;
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
            _composeOwnershipTransferred = false;
        }

        private static bool HasTopologyCompositionChange(
            NavigationMapInstance? prior,
            NavigationMapInstance next)
        {
            if (prior == null
                || !ReferenceEquals(prior.Map, next.Map)
                || prior.BakeVersion != next.BakeVersion
                || !ReferenceEquals(prior.BakedCellLookup, next.BakedCellLookup))
            {
                return true;
            }

            NavigationGridGenerationIdentity priorIdentity = prior.GridIdentity;
            NavigationGridGenerationIdentity nextIdentity = next.GridIdentity;
            return priorIdentity.WorldSpawnToken != nextIdentity.WorldSpawnToken
                || priorIdentity.GridIndex != nextIdentity.GridIndex
                || priorIdentity.GridSpawnToken != nextIdentity.GridSpawnToken
                || !priorIdentity.ConfigurationKey.Equals(nextIdentity.ConfigurationKey);
        }

        private void RecordPersistentCopies(int copiedNodes, long bytesPerNode)
        {
            _copiedPersistentPages = checked(_copiedPersistentPages + copiedNodes);
            _copiedPersistentBytes = checked(
                _copiedPersistentBytes + (copiedNodes * bytesPerNode));
        }
    }
}
