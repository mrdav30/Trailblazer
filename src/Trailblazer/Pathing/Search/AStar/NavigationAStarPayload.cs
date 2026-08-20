//=======================================================================
// NavigationAStarPayload.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using System;
using System.Runtime.CompilerServices;

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
        NavigationAStarGuidePoint[] guidePoints,
        Fixed64 cost,
        GraphDependencyStamp dependencies,
        NavigationSurfaceAStarStatus status)
    {
        SwiftThrowHelper.ThrowIfNull(guidePoints, nameof(guidePoints));
        SwiftThrowHelper.ThrowIfNull(dependencies, nameof(dependencies));
        if (!IsReusableResult(status, guidePoints.Length))
        {
            throw new ArgumentException(
                "Only successful paths and exact no-path results are reusable.",
                nameof(status));
        }
        Key = key;
        GuidePoints = guidePoints;
        Cost = cost;
        Dependencies = dependencies;
        Status = status;
    }

    internal NavigationAStarPayloadKey Key { get; }

    internal NavigationAStarGuidePoint[] GuidePoints { get; }

    internal bool HasPath => GuidePoints.Length != 0;

    internal Fixed64 Cost { get; }

    internal GraphDependencyStamp Dependencies { get; }

    internal NavigationSurfaceAStarStatus Status { get; }

    internal long RetainedBytes => GetRetainedBytes(
        GuidePoints.Length,
        Dependencies);

    internal static long GetRetainedBytes(
        int guidePointCount,
        GraphDependencyStamp dependencies)
    {
        SwiftThrowHelper.ThrowIfNegative(guidePointCount, nameof(guidePointCount));
        SwiftThrowHelper.ThrowIfNull(dependencies, nameof(dependencies));
        return checked(
        BaseRetainedBytes
        + GetGuidePointArrayRetainedBytes(guidePointCount)
        + dependencies.RetainedBytes);
    }

    internal static long GetMaximumRetainedBytes(
        int guidePointCount,
        int componentCount,
        int pageCount)
    {
        SwiftThrowHelper.ThrowIfNegative(guidePointCount, nameof(guidePointCount));
        return checked(
            BaseRetainedBytes
            + GetGuidePointArrayRetainedBytes(guidePointCount)
            + GraphDependencyStamp.GetRetainedBytes(componentCount, pageCount));
    }

    internal static bool IsReusableResult(
        NavigationSurfaceAStarStatus status,
        int guidePointCount) =>
        (status == NavigationSurfaceAStarStatus.Success && guidePointCount > 0)
        || (status == NavigationSurfaceAStarStatus.NoPath && guidePointCount == 0);

    private static long GetGuidePointArrayRetainedBytes(int length) =>
        length == 0
            ? 0L
            : Align8(checked(
                ArrayHeaderBytes
                + ((long)length * Unsafe.SizeOf<NavigationAStarGuidePoint>())));

    private static long Align8(long value) => checked((value + 7L) & ~7L);
}
