//=======================================================================
// EditSession.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Contains the EditSession companion type.</content>
internal sealed partial class NavigationAutomaticSeamIndex
{
    /// <summary>Stages copy-on-write automatic seam index edits for publication.</summary>
    internal sealed class EditSession
    {
        private const long BaseRetainedBytes = 160L;
        private readonly NavigationAutomaticSeamIndex _source;
        private readonly NavigationSeamEditTree<NavigationAutomaticSeamPairKey, NavigationAutomaticSeamPairRecord>.Editor _pairs;
        private readonly NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>>.Editor _dependencies;
        private readonly NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>>.Editor _active;
        private readonly NavigationSeamEditTree<NavigationAutomaticSeamMapKey, NavigationPagedSequence<NavigationStructuralLink>>.Editor _links;
        private long _pairBytes;
        private long _dependencyRowBytes;
        private long _activeRowBytes;
        private long _linkRowBytes;
        private int _pairPages;
        private int _dependencyRowPages;
        private int _activeRowPages;
        private int _linkRowPages;
        private long _additionalPairBytes;
        private long _additionalDependencyRowBytes;
        private long _additionalActiveRowBytes;
        private long _additionalLinkRowBytes;
        private int _additionalPairPages;
        private int _additionalDependencyRowPages;
        private int _additionalActiveRowPages;
        private int _additionalLinkRowPages;
        private bool _sealed;

        internal EditSession(
            NavigationAutomaticSeamIndex source,
            NavigationSeamEditToken ownershipToken)
        {
            _source = source;
            _pairs = source._pairs.Edit(ownershipToken);
            _dependencies = source._dependencies.Edit(ownershipToken);
            _active = source._active.Edit(ownershipToken);
            _links = source._links.Edit(ownershipToken);
            _pairBytes = source._pairBytes;
            _dependencyRowBytes = source._dependencyRowBytes;
            _activeRowBytes = source._activeRowBytes;
            _linkRowBytes = source._linkRowBytes;
            _pairPages = source._pairPages;
            _dependencyRowPages = source._dependencyRowPages;
            _activeRowPages = source._activeRowPages;
            _linkRowPages = source._linkRowPages;
        }

        internal long RetainedBytes => checked(
            BaseRetainedBytes
            + _pairs.RetainedBytes
            + _dependencies.RetainedBytes
            + _active.RetainedBytes
            + _links.RetainedBytes
            + _additionalPairBytes
            + _additionalDependencyRowBytes
            + _additionalActiveRowBytes
            + _additionalLinkRowBytes);

        internal int PersistentPageCount => checked(
            1
            + _pairs.PersistentPageCount
            + _dependencies.PersistentPageCount
            + _active.PersistentPageCount
            + _links.PersistentPageCount
            + _additionalPairPages
            + _additionalDependencyRowPages
            + _additionalActiveRowPages
            + _additionalLinkRowPages);

        internal bool IsChanged =>
            _pairs.IsChanged || _dependencies.IsChanged || _active.IsChanged || _links.IsChanged;

        internal long SealedAdditionalRetainedBytes => !IsChanged
            ? 0L
            : checked(
                NavigationAutomaticSeamIndex.BaseRetainedBytes
                + GetSealedTreeBytes(_pairs, PairNodeBytes)
                + GetSealedTreeBytes(_dependencies, AddressNodeBytes)
                + GetSealedTreeBytes(_active, AddressNodeBytes)
                + GetSealedTreeBytes(_links, MapNodeBytes)
                + _additionalPairBytes
                + _additionalDependencyRowBytes
                + _additionalActiveRowBytes
                + _additionalLinkRowBytes);

        internal int SealedAdditionalPersistentPages => !IsChanged
            ? 0
            : checked(
                GetSealedTreePages(_pairs)
                + GetSealedTreePages(_dependencies)
                + GetSealedTreePages(_active)
                + GetSealedTreePages(_links)
                + _additionalPairPages
                + _additionalDependencyRowPages
                + _additionalActiveRowPages
                + _additionalLinkRowPages);

        internal void SetPair(
            NavigationAutomaticSeamPairKey key,
            NavigationAutomaticSeamPairRecord? record)
        {
            _pairs.TryGetValue(key, out NavigationAutomaticSeamPairRecord prior);
            _source._pairs.TryGetValue(key, out NavigationAutomaticSeamPairRecord shared);
            ReplacePairPayload(prior, record, shared);
            if (prior != null)
            {
                _pairBytes -= NavigationAutomaticSeamPair.RetainedSize
                    + NavigationAutomaticSeamPairRecord.RetainedSize;
                _pairPages -= 2;
            }
            if (record == null)
                _pairs.Remove(key);
            else
            {
                _pairs.Set(key, record);
                _pairBytes += NavigationAutomaticSeamPair.RetainedSize
                    + NavigationAutomaticSeamPairRecord.RetainedSize;
                _pairPages += 2;
            }
        }

        internal void SetDependencyRow(
            NavigationCellAddress address,
            NavigationPagedSequence<NavigationAutomaticSeamPair> row) => SetRow(
                address,
                row,
                _source._dependencies,
                _dependencies,
                ref _dependencyRowBytes,
                ref _dependencyRowPages,
                ref _additionalDependencyRowBytes,
                ref _additionalDependencyRowPages);

        internal void SetActiveRow(
            NavigationCellAddress address,
            NavigationPagedSequence<NavigationAutomaticSeamPair> row) => SetRow(
                address,
                row,
                _source._active,
                _active,
                ref _activeRowBytes,
                ref _activeRowPages,
                ref _additionalActiveRowBytes,
                ref _additionalActiveRowPages);

        internal void SetStructuralLinks(
            string mapId,
            NavigationPagedSequence<NavigationStructuralLink> row)
        {
            var key = new NavigationAutomaticSeamMapKey(mapId);
            _links.TryGetValue(key, out NavigationPagedSequence<NavigationStructuralLink> prior);
            _source._links.TryGetValue(key, out NavigationPagedSequence<NavigationStructuralLink> shared);
            prior ??= NavigationPagedSequence<NavigationStructuralLink>.Empty;
            shared ??= NavigationPagedSequence<NavigationStructuralLink>.Empty;
            ReplacePayload(
                prior,
                row,
                shared,
                row.RetainedBytes,
                row.PersistentPageCount,
                ref _additionalLinkRowBytes,
                ref _additionalLinkRowPages);
            _linkRowBytes = checked(_linkRowBytes - prior.RetainedBytes + row.RetainedBytes);
            _linkRowPages = checked(
                _linkRowPages - prior.PersistentPageCount + row.PersistentPageCount);
            if (row.Count == 0)
                _links.Remove(key);
            else
                _links.Set(key, row);
        }

        internal NavigationAutomaticSeamIndex Seal()
        {
            ChronicleGuard.ThrowIfTrue(
                _sealed,
                message: "The seam index edit is already sealed.");
            _sealed = true;
            if (!IsChanged)
                return _source;
            return new NavigationAutomaticSeamIndex(
                _pairs.Seal(),
                _dependencies.Seal(),
                _active.Seal(),
                _links.Seal(),
                _pairBytes,
                _dependencyRowBytes,
                _activeRowBytes,
                _linkRowBytes,
                _pairPages,
                _dependencyRowPages,
                _activeRowPages,
                _linkRowPages);
        }

        private void ReplacePairPayload(
            NavigationAutomaticSeamPairRecord? prior,
            NavigationAutomaticSeamPairRecord? next,
            NavigationAutomaticSeamPairRecord? shared)
        {
            if (prior != null && !ReferenceEquals(prior, shared))
            {
                _additionalPairBytes -= NavigationAutomaticSeamPairRecord.RetainedSize;
                _additionalPairPages--;
                if (shared == null || !ReferenceEquals(prior.Pair, shared.Pair))
                {
                    _additionalPairBytes -= NavigationAutomaticSeamPair.RetainedSize;
                    _additionalPairPages--;
                }
            }
            if (next == null || ReferenceEquals(next, shared))
                return;
            _additionalPairBytes = checked(
                _additionalPairBytes + NavigationAutomaticSeamPairRecord.RetainedSize);
            _additionalPairPages++;
            if (shared == null || !ReferenceEquals(next.Pair, shared.Pair))
            {
                _additionalPairBytes = checked(
                    _additionalPairBytes + NavigationAutomaticSeamPair.RetainedSize);
                _additionalPairPages++;
            }
        }

        private static void SetRow(
            NavigationCellAddress address,
            NavigationPagedSequence<NavigationAutomaticSeamPair> row,
            NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>> source,
            NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>>.Editor editor,
            ref long rowBytes,
            ref int rowPages,
            ref long additionalBytes,
            ref int additionalPages)
        {
            editor.TryGetValue(address, out NavigationPagedSequence<NavigationAutomaticSeamPair> prior);
            source.TryGetValue(address, out NavigationPagedSequence<NavigationAutomaticSeamPair> shared);
            prior ??= NavigationPagedSequence<NavigationAutomaticSeamPair>.Empty;
            shared ??= NavigationPagedSequence<NavigationAutomaticSeamPair>.Empty;
            ReplacePayload(
                prior,
                row,
                shared,
                row.RetainedBytes,
                row.PersistentPageCount,
                ref additionalBytes,
                ref additionalPages);
            rowBytes = checked(rowBytes - prior.RetainedBytes + row.RetainedBytes);
            rowPages = checked(rowPages - prior.PersistentPageCount + row.PersistentPageCount);
            if (row.Count == 0)
                editor.Remove(address);
            else
                editor.Set(address, row);
        }

        private static void ReplacePayload<T>(
            T prior,
            T next,
            T shared,
            long nextBytes,
            int nextPages,
            ref long bytes,
            ref int pages)
            where T : class
        {
            if (!ReferenceEquals(prior, shared))
            {
                GetPayloadSize(prior, out long priorBytes, out int priorPages);
                bytes -= priorBytes;
                pages -= priorPages;
            }
            if (!ReferenceEquals(next, shared))
            {
                bytes = checked(bytes + nextBytes);
                pages = checked(pages + nextPages);
            }
        }

        private static void GetPayloadSize<T>(T value, out long bytes, out int pages)
            where T : class
        {
            if (value is NavigationPagedSequence<NavigationAutomaticSeamPair> seamRow)
            {
                bytes = seamRow.RetainedBytes;
                pages = seamRow.PersistentPageCount;
                return;
            }
            var linkRow = (NavigationPagedSequence<NavigationStructuralLink>)(object)value;
            bytes = linkRow.RetainedBytes;
            pages = linkRow.PersistentPageCount;
        }

        private static long GetSealedTreeBytes<TK, TV>(
            NavigationSeamEditTree<TK, TV>.Editor editor,
            int nodeBytes)
            where TK : struct, System.IComparable<TK>
            where TV : class => editor.IsChanged
                ? checked(32L + ((long)editor.OwnedNodeCount * nodeBytes))
                : 0L;

        private static int GetSealedTreePages<TK, TV>(
            NavigationSeamEditTree<TK, TV>.Editor editor)
            where TK : struct, System.IComparable<TK>
            where TV : class => editor.IsChanged ? checked(1 + editor.OwnedNodeCount) : 0;
    }
}
