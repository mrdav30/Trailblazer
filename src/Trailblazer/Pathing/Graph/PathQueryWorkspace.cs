//=======================================================================
// PathQueryWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Owns exclusive mutable scratch for one admitted graph query.</summary>
internal sealed class PathQueryWorkspace
{
    internal PathQueryWorkspace(int nodeCapacity)
    {
        NodeHeap = new int[nodeCapacity];
        NodeMetadata = new long[nodeCapacity];
        EdgeScratch = new int[checked(nodeCapacity * 2)];
        RetainedBytes = checked(64L + ((long)nodeCapacity * 24L));
    }

    internal int[] NodeHeap { get; }

    internal long[] NodeMetadata { get; }

    internal int[] EdgeScratch { get; }

    internal int NodeCapacity => NodeHeap.Length;

    internal long RetainedBytes { get; }

    internal void Clear()
    {
        Array.Clear(NodeHeap, 0, NodeHeap.Length);
        Array.Clear(NodeMetadata, 0, NodeMetadata.Length);
        Array.Clear(EdgeScratch, 0, EdgeScratch.Length);
    }
}
