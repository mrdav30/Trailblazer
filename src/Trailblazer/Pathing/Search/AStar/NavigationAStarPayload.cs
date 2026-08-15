//=======================================================================
// NavigationAStarPayload.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using System;

namespace Trailblazer.Pathing;

/// <summary>Stores one immutable fixed-point A* result independent of graph slots.</summary>
internal sealed class NavigationAStarPayload
{
    private const long ObjectHeaderBytes = 16L;
    private const long ArrayHeaderBytes = 24L;
    private const long ReferenceSlotBytes = 8L;
    private const long Int64Bytes = 8L;
    private const long ByteBytes = 1L;
    private const long Fixed64Bytes = Int64Bytes;
    private const long NavigationCellAddressBytes = 24L;
    private const long PathQueryBytes = 240L;
    private const long NavigationAStarPayloadKeyBytes =
        PathQueryBytes + (2L * NavigationCellAddressBytes);
    private static readonly long BaseRetainedBytes = Align8(
        ObjectHeaderBytes
        + NavigationAStarPayloadKeyBytes
        + (2L * ReferenceSlotBytes)
        + Fixed64Bytes
        + ByteBytes);

    internal NavigationAStarPayload(
        NavigationAStarPayloadKey key,
        NavigationCellAddress[] nodes,
        Fixed64 cost,
        GraphDependencyStamp dependencies,
        NavigationSurfaceAStarStatus status)
    {
        SwiftThrowHelper.ThrowIfNull(nodes, nameof(nodes));
        SwiftThrowHelper.ThrowIfNull(dependencies, nameof(dependencies));
        if (!IsReusableResult(status, nodes.Length))
        {
            throw new ArgumentException(
                "Only successful paths and exact no-path results are reusable.",
                nameof(status));
        }
        Key = key;
        Nodes = nodes;
        Cost = cost;
        Dependencies = dependencies;
        Status = status;
    }

    internal NavigationAStarPayloadKey Key { get; }

    internal NavigationCellAddress[] Nodes { get; }

    internal bool HasPath => Nodes.Length != 0;

    internal Fixed64 Cost { get; }

    internal GraphDependencyStamp Dependencies { get; }

    internal NavigationSurfaceAStarStatus Status { get; }

    internal long RetainedBytes => GetRetainedBytes(
        Nodes.Length,
        Dependencies);

    internal static long GetRetainedBytes(
        int nodeCount,
        GraphDependencyStamp dependencies)
    {
        SwiftThrowHelper.ThrowIfNegative(nodeCount, nameof(nodeCount));
        SwiftThrowHelper.ThrowIfNull(dependencies, nameof(dependencies));
        return checked(
        BaseRetainedBytes
        + GetNodeArrayRetainedBytes(nodeCount)
        + dependencies.RetainedBytes);
    }

    internal static long GetMaximumRetainedBytes(
        int nodeCount,
        int componentCount,
        int pageCount)
    {
        SwiftThrowHelper.ThrowIfNegative(nodeCount, nameof(nodeCount));
        return checked(
            BaseRetainedBytes
            + GetNodeArrayRetainedBytes(nodeCount)
            + GraphDependencyStamp.GetRetainedBytes(componentCount, pageCount));
    }

    internal static bool IsReusableResult(
        NavigationSurfaceAStarStatus status,
        int nodeCount) =>
        (status == NavigationSurfaceAStarStatus.Success && nodeCount > 0)
        || (status == NavigationSurfaceAStarStatus.NoPath && nodeCount == 0);

    private static long GetNodeArrayRetainedBytes(int length) =>
        length == 0
            ? 0L
            : Align8(checked(ArrayHeaderBytes + ((long)length * NavigationCellAddressBytes)));

    private static long Align8(long value) => checked((value + 7L) & ~7L);
}
