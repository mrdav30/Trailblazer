//=======================================================================
// NavigationTransitionPageRoot.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Owns immutable transition pages without adding a second edge registry or cache.</summary>
internal sealed class NavigationTransitionPageRoot
{
    internal static readonly NavigationTransitionPageRoot Empty = new(
        PersistentStringMap<PersistentIntMap<NavigationTransitionPage>>.Empty,
        innerMapBytes: 0,
        pageBytes: 0,
        innerMapPages: 0,
        pagePages: 0);

    private readonly PersistentStringMap<PersistentIntMap<NavigationTransitionPage>> _maps;
    private readonly long _innerMapBytes;
    private readonly long _pageBytes;
    private readonly int _innerMapPages;
    private readonly int _pagePages;

    private NavigationTransitionPageRoot(
        PersistentStringMap<PersistentIntMap<NavigationTransitionPage>> maps,
        long innerMapBytes,
        long pageBytes,
        int innerMapPages,
        int pagePages)
    {
        _maps = maps;
        _innerMapBytes = innerMapBytes;
        _pageBytes = pageBytes;
        _innerMapPages = innerMapPages;
        _pagePages = pagePages;
    }

    internal long RetainedBytes => checked(
        56L + _maps.RetainedBytes + _innerMapBytes + _pageBytes);

    internal int PersistentPageCount => checked(
        1 + _maps.PersistentNodeCount + _innerMapPages + _pagePages);

    internal bool TryGet(
        NavigationTransitionPageAddress address,
        out NavigationTransitionPage page)
    {
        if (_maps.TryGetValue(
                address.MapId,
                out PersistentIntMap<NavigationTransitionPage> map)
            && map.TryGetValue(address.PageIndex, out page!))
        {
            return true;
        }
        page = null!;
        return false;
    }

    internal NavigationTransitionPageRoot Set(
        NavigationTransitionPage page,
        out int copiedNodes) => Set(page, out copiedNodes, out _);

    internal NavigationTransitionPageRoot Set(
        NavigationTransitionPage page,
        out int copiedNodes,
        out long copiedBytes)
    {
        copiedNodes = 0;
        copiedBytes = 0;
        bool hadMap = _maps.TryGetValue(
            page.Address.MapId,
            out PersistentIntMap<NavigationTransitionPage> map);
        map ??= PersistentIntMap<NavigationTransitionPage>.Empty;
        map.TryGetValue(page.Address.PageIndex, out NavigationTransitionPage prior);
        long innerBytes = _innerMapBytes - (hadMap ? map.RetainedBytes : 0L);
        int innerPages = _innerMapPages - (hadMap ? map.PersistentNodeCount : 0);
        int copied;
        if (page.IsEmpty)
            map = map.Remove(page.Address.PageIndex, out copied);
        else
            map = map.Set(page.Address.PageIndex, page, out copied);
        copiedNodes = checked(copiedNodes + copied);
        copiedBytes = checked(copiedBytes + ((long)copied * 72L));
        PersistentStringMap<PersistentIntMap<NavigationTransitionPage>> maps;
        if (map.Count == 0)
            maps = _maps.Remove(page.Address.MapId, out _, out copied);
        else
        {
            innerBytes = checked(innerBytes + map.RetainedBytes);
            innerPages = checked(innerPages + map.PersistentNodeCount);
            maps = _maps.Set(page.Address.MapId, map, out copied);
        }
        copiedNodes = checked(copiedNodes + copied);
        copiedBytes = checked(copiedBytes + ((long)copied * 64L));
        return new NavigationTransitionPageRoot(
            maps,
            innerBytes,
            checked(_pageBytes - (prior?.RetainedBytes ?? 0L) + (page.IsEmpty ? 0L : page.RetainedBytes)),
            innerPages,
            checked(_pagePages - (prior?.PersistentPageCount ?? 0) + (page.IsEmpty ? 0 : page.PersistentPageCount)));
    }
}
