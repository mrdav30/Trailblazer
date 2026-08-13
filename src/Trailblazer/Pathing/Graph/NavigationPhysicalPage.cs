//=======================================================================
// NavigationPhysicalPage.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

internal sealed class NavigationPhysicalPage
{
    internal const int SlotCount = 64;

    internal NavigationPhysicalPage(int pageIndex, long version = 0)
    {
        PageIndex = pageIndex;
        Version = version;
        IsPresent = new bool[SlotCount];
        ObstacleCounts = new byte[SlotCount];
    }

    private NavigationPhysicalPage(int pageIndex, long version, bool[] isPresent, byte[] obstacleCounts)
    {
        PageIndex = pageIndex;
        Version = version;
        IsPresent = isPresent;
        ObstacleCounts = obstacleCounts;
    }

    internal int PageIndex { get; }

    internal long Version { get; }

    internal bool[] IsPresent { get; }

    internal byte[] ObstacleCounts { get; }

    internal NavigationPhysicalPage Clone(long version) => new(
        PageIndex,
        version,
        (bool[])IsPresent.Clone(),
        (byte[])ObstacleCounts.Clone());
}
