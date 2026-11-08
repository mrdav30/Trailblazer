//=======================================================================
// NavigationAutomaticSeamIndex.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Stores canonical seam geometry plus dependency, active-endpoint, and link incidence.</summary>
internal sealed partial class NavigationAutomaticSeamIndex
{
    internal const int PairNodeBytes = 104;
    internal const int AddressNodeBytes = 80;
    internal const int MapNodeBytes = 64;
    internal const int PairCursorShellBytes = 144;
    internal const int AddressCursorShellBytes = 96;
    internal const int MapCursorShellBytes = 64;
    internal const int LinkCursorShellBytes = 80;
    private const long BaseRetainedBytes = 96L;

    internal static readonly NavigationAutomaticSeamIndex Empty = new(
        new NavigationSeamEditTree<NavigationAutomaticSeamPairKey, NavigationAutomaticSeamPairRecord>(PairNodeBytes),
        new NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>>(AddressNodeBytes),
        new NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>>(AddressNodeBytes),
        new NavigationSeamEditTree<NavigationAutomaticSeamMapKey, NavigationPagedSequence<NavigationStructuralLink>>(MapNodeBytes),
        0, 0, 0, 0, 0, 0, 0, 0);

    private readonly NavigationSeamEditTree<NavigationAutomaticSeamPairKey, NavigationAutomaticSeamPairRecord> _pairs;
    private readonly NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>> _dependencies;
    private readonly NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>> _active;
    private readonly NavigationSeamEditTree<NavigationAutomaticSeamMapKey, NavigationPagedSequence<NavigationStructuralLink>> _links;
    private readonly long _pairBytes;
    private readonly long _dependencyRowBytes;
    private readonly long _activeRowBytes;
    private readonly long _linkRowBytes;
    private readonly int _pairPages;
    private readonly int _dependencyRowPages;
    private readonly int _activeRowPages;
    private readonly int _linkRowPages;

    private NavigationAutomaticSeamIndex(
        NavigationSeamEditTree<NavigationAutomaticSeamPairKey, NavigationAutomaticSeamPairRecord> pairs,
        NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>> dependencies,
        NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>> active,
        NavigationSeamEditTree<NavigationAutomaticSeamMapKey, NavigationPagedSequence<NavigationStructuralLink>> links,
        long pairBytes,
        long dependencyRowBytes,
        long activeRowBytes,
        long linkRowBytes,
        int pairPages,
        int dependencyRowPages,
        int activeRowPages,
        int linkRowPages)
    {
        _pairs = pairs;
        _dependencies = dependencies;
        _active = active;
        _links = links;
        _pairBytes = pairBytes;
        _dependencyRowBytes = dependencyRowBytes;
        _activeRowBytes = activeRowBytes;
        _linkRowBytes = linkRowBytes;
        _pairPages = pairPages;
        _dependencyRowPages = dependencyRowPages;
        _activeRowPages = activeRowPages;
        _linkRowPages = linkRowPages;
    }

    internal int PairCount => _pairs.Count;

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + _pairs.RetainedBytes
        + _dependencies.RetainedBytes
        + _active.RetainedBytes
        + _links.RetainedBytes
        + _pairBytes
        + _dependencyRowBytes
        + _activeRowBytes
        + _linkRowBytes);

    internal int PersistentPageCount => checked(
        _pairs.PersistentPageCount
        + _dependencies.PersistentPageCount
        + _active.PersistentPageCount
        + _links.PersistentPageCount
        + _pairPages
        + _dependencyRowPages
        + _activeRowPages
        + _linkRowPages);

    internal bool TryGetPair(
        NavigationCellAddress first,
        NavigationCellAddress second,
        out NavigationAutomaticSeamPair pair)
    {
        if (_pairs.TryGetValue(
                new NavigationAutomaticSeamPairKey(first, second),
                out NavigationAutomaticSeamPairRecord record))
        {
            pair = record.Pair;
            return true;
        }
        pair = null!;
        return false;
    }

    internal bool TryGetPairRecord(
        NavigationAutomaticSeamPairKey key,
        out NavigationAutomaticSeamPairRecord record) => _pairs.TryGetValue(key, out record);

    internal bool IsActive(NavigationAutomaticSeamRef seam) =>
        _pairs.TryGetValue(
            new NavigationAutomaticSeamPairKey(seam.Pair.First, seam.Pair.Second),
            out NavigationAutomaticSeamPairRecord record)
        && ReferenceEquals(record.Pair, seam.Pair)
        && record.IsActive;

    internal NavigationPagedSequence<NavigationAutomaticSeamPair> GetDependencyRow(
        NavigationCellAddress address) => GetRow(_dependencies, address);

    internal NavigationPagedSequence<NavigationAutomaticSeamPair> GetActiveRow(
        NavigationCellAddress address) => GetRow(_active, address);

    internal EndpointEnumerator GetActiveEndpointEnumerator(NavigationCellAddress address) =>
        new(address, GetActiveRow(address));

    internal NavigationPagedSequence<NavigationStructuralLink> GetStructuralLinks(string mapId) =>
        _links.TryGetValue(
            new NavigationAutomaticSeamMapKey(mapId),
            out NavigationPagedSequence<NavigationStructuralLink> links)
            ? links
            : NavigationPagedSequence<NavigationStructuralLink>.Empty;

    internal EditSession Edit(NavigationSeamEditToken ownershipToken) =>
        new(this, ownershipToken);

    internal NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>>.Cursor
        CreateDependencyCursor(int maximumHeight) =>
        _dependencies.CreateCursor(maximumHeight, AddressCursorShellBytes);

    internal void BeginDependencyRange(
        NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>>.Cursor cursor,
        string mapId)
    {
        cursor.BeginAtLeast(
            _dependencies,
            new NavigationCellAddress(
                mapId,
                new GridForge.Spatial.VoxelIndex(int.MinValue, int.MinValue, int.MinValue)));
    }

    private static NavigationPagedSequence<NavigationAutomaticSeamPair> GetRow(
        NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>> root,
        NavigationCellAddress address) => root.TryGetValue(address, out NavigationPagedSequence<NavigationAutomaticSeamPair> row)
            ? row
            : NavigationPagedSequence<NavigationAutomaticSeamPair>.Empty;
}
