//=======================================================================
// NavigationInstanceDirectory.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Stores immutable map instances in an ordinal persistent MapId tree.</summary>
internal sealed class NavigationInstanceDirectory
{
    private readonly PersistentStringMap<NavigationMapInstance> _instances;

    private NavigationInstanceDirectory(PersistentStringMap<NavigationMapInstance> instances)
    {
        _instances = instances;
    }

    internal int Count => _instances.Count;

    internal long RetainedBytes => _instances.RetainedBytes;

    internal int PersistentPageCount => 1 + _instances.PersistentNodeCount;

    internal static NavigationInstanceDirectory Create(NavigationMapInstance[] instances)
    {
        PersistentStringMap<NavigationMapInstance> map = PersistentStringMap<NavigationMapInstance>.Empty;
        for (int i = 0; i < instances.Length; i++)
            map = map.Set(instances[i].MapId, instances[i]);
        return new NavigationInstanceDirectory(map);
    }

    internal NavigationMapInstance Get(int ordinal) => _instances.GetValueAt(ordinal);

    internal string GetMapId(int ordinal) => _instances.GetKeyAt(ordinal);

    internal bool TryGet(string mapId, out NavigationMapInstance instance) =>
        _instances.TryGetValue(mapId, out instance!);

    internal NavigationInstanceDirectory Set(
        string mapId,
        NavigationMapInstance instance,
        out int copiedNodeCount) =>
        new(_instances.Set(mapId, instance, out copiedNodeCount));

    internal NavigationInstanceDirectory Remove(
        string mapId,
        out bool removed,
        out int copiedNodeCount) =>
        new(_instances.Remove(mapId, out removed, out copiedNodeCount));

    internal NavigationInstanceDirectory With(int ordinal, NavigationMapInstance instance) =>
        new(_instances.Set(GetMapId(ordinal), instance));
}
