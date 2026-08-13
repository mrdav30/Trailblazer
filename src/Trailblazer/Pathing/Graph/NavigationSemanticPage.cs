//=======================================================================
// NavigationSemanticPage.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

internal sealed class NavigationSemanticPage
{
    internal const int SlotCount = 64;

    internal NavigationSemanticPage(int pageIndex, long version = 0)
    {
        PageIndex = pageIndex;
        Version = version;
        HasOverride = new bool[SlotCount];
        IsSuppressed = new bool[SlotCount];
        Cells = new NavigationCell[SlotCount];
    }

    internal int PageIndex { get; }

    internal long Version { get; }

    internal bool[] HasOverride { get; }

    internal bool[] IsSuppressed { get; }

    internal NavigationCell[] Cells { get; }

    internal NavigationSemanticPage Clone(long version)
    {
        var clone = new NavigationSemanticPage(PageIndex, version);
        System.Array.Copy(HasOverride, clone.HasOverride, SlotCount);
        System.Array.Copy(IsSuppressed, clone.IsSuppressed, SlotCount);
        System.Array.Copy(Cells, clone.Cells, SlotCount);
        return clone;
    }

    internal bool IsEmpty()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (HasOverride[i] || IsSuppressed[i])
                return false;
        }
        return true;
    }
}
