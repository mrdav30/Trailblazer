//=======================================================================
// NavigationAStarPayload.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;

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
    private const long NullableUInt64Bytes = 16L;
    private static readonly long BaseRetainedBytes = Align8(
        ObjectHeaderBytes
        + Unsafe.SizeOf<NavigationAStarPayloadKey>()
        + (3L * ReferenceSlotBytes)
        + Fixed64Bytes
        + NullableUInt64Bytes
        + ByteBytes);

    internal NavigationAStarPayload(
        NavigationAStarPayloadKey key,
        NavigationAStarGuidePoint[] guidePoints,
        NavigationTransitionInstruction[] transitionInstructions,
        Fixed64 cost,
        GraphDependencyStamp dependencies,
        ulong? worldChangeSequence,
        NavigationSurfaceAStarStatus status)
    {
        SwiftThrowHelper.ThrowIfNull(guidePoints, nameof(guidePoints));
        SwiftThrowHelper.ThrowIfNull(
            transitionInstructions,
            nameof(transitionInstructions));
        SwiftThrowHelper.ThrowIfNull(dependencies, nameof(dependencies));
        if (!IsReusableResult(status, guidePoints.Length))
        {
            throw new ArgumentException(
                "Only successful paths and exact no-path results are reusable.",
                nameof(status));
        }
        Key = key;
        GuidePoints = guidePoints;
        TransitionInstructions = transitionInstructions;
        Cost = cost;
        Dependencies = dependencies;
        WorldChangeSequence = worldChangeSequence;
        Status = status;
    }

    internal NavigationAStarPayloadKey Key { get; }

    internal NavigationAStarGuidePoint[] GuidePoints { get; }

    internal NavigationTransitionInstruction[] TransitionInstructions { get; }

    internal bool HasPath => GuidePoints.Length != 0;

    internal Fixed64 Cost { get; }

    internal GraphDependencyStamp Dependencies { get; }

    internal ulong? WorldChangeSequence { get; }

    internal NavigationSurfaceAStarStatus Status { get; }

    internal long RetainedBytes => GetRetainedBytes(
        GuidePoints.Length,
        TransitionInstructions.Length,
        Dependencies);

    internal static long GetRetainedBytes(
        int guidePointCount,
        int transitionInstructionCount,
        GraphDependencyStamp dependencies)
    {
        SwiftThrowHelper.ThrowIfNegative(guidePointCount, nameof(guidePointCount));
        SwiftThrowHelper.ThrowIfNegative(
            transitionInstructionCount,
            nameof(transitionInstructionCount));
        SwiftThrowHelper.ThrowIfNull(dependencies, nameof(dependencies));
        return checked(
        BaseRetainedBytes
        + GetGuidePointArrayRetainedBytes(guidePointCount)
        + GetTransitionInstructionArrayRetainedBytes(transitionInstructionCount)
        + dependencies.RetainedBytes);
    }

    internal static long GetMaximumRetainedBytes(
        int guidePointCount,
        int transitionInstructionCount,
        int componentCount,
        int pageCount)
    {
        SwiftThrowHelper.ThrowIfNegative(guidePointCount, nameof(guidePointCount));
        SwiftThrowHelper.ThrowIfNegative(
            transitionInstructionCount,
            nameof(transitionInstructionCount));
        return checked(
            BaseRetainedBytes
            + GetGuidePointArrayRetainedBytes(guidePointCount)
            + GetTransitionInstructionArrayRetainedBytes(transitionInstructionCount)
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

    private static long GetTransitionInstructionArrayRetainedBytes(int length) =>
        length == 0
            ? 0L
            : Align8(checked(
                ArrayHeaderBytes
                + ((long)length * Unsafe.SizeOf<NavigationTransitionInstruction>())));

    private static long Align8(long value) => checked((value + 7L) & ~7L);
}
