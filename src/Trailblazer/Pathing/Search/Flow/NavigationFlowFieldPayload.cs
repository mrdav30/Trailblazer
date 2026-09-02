//=======================================================================
// NavigationFlowFieldPayload.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Stores one immutable stable-address flow-field prefix.</summary>
internal sealed class NavigationFlowFieldPayload
{
    private const long ObjectHeaderBytes = 16L;
    private const long ArrayHeaderBytes = 24L;
    private const long ReferenceSlotBytes = 8L;
    private const long Int32Bytes = 4L;
    private const long NullableUInt64Bytes = 16L;
    private const long BooleanBytes = 1L;
    private static readonly long BaseRetainedBytes = Align8(
        ObjectHeaderBytes
        + Unsafe.SizeOf<NavigationFlowFieldPayloadKey>()
        + (4L * ReferenceSlotBytes)
        + NullableUInt64Bytes
        + BooleanBytes);

    internal NavigationFlowFieldPayload(
        NavigationFlowFieldPayloadKey key,
        NavigationFlowFieldNode[] nodes,
        int[] addressLookupOrdinals,
        NavigationTransitionInstruction[] transitionInstructions,
        GraphDependencyStamp dependencies,
        bool isComplete,
        ulong? worldChangeSequence)
    {
        SwiftThrowHelper.ThrowIfNull(nodes, nameof(nodes));
        SwiftThrowHelper.ThrowIfNull(addressLookupOrdinals, nameof(addressLookupOrdinals));
        SwiftThrowHelper.ThrowIfNull(
            transitionInstructions,
            nameof(transitionInstructions));
        SwiftThrowHelper.ThrowIfNull(dependencies, nameof(dependencies));
        SwiftThrowHelper.ThrowIfArgument(
            nodes.Length == 0 || addressLookupOrdinals.Length != nodes.Length,
            paramName: null,
            message: "Flow payload arrays must be non-empty and aligned.");
        Key = key;
        Nodes = nodes;
        AddressLookupOrdinals = addressLookupOrdinals;
        TransitionInstructions = transitionInstructions;
        Dependencies = dependencies;
        IsComplete = isComplete;
        WorldChangeSequence = worldChangeSequence;
    }

    internal NavigationFlowFieldPayloadKey Key { get; }
    internal NavigationFlowFieldNode[] Nodes { get; }
    internal int[] AddressLookupOrdinals { get; }
    internal NavigationTransitionInstruction[] TransitionInstructions { get; }
    internal GraphDependencyStamp Dependencies { get; }
    internal bool IsComplete { get; }
    internal ulong? WorldChangeSequence { get; }
    internal Fixed64 LastSettledCost => Nodes[Nodes.Length - 1].IntegrationCost;
    internal NavigationCellAddress LastSettledAddress => Nodes[Nodes.Length - 1].Address;

    internal long RetainedBytes => GetRetainedBytes(
        Nodes.Length,
        TransitionInstructions.Length,
        Dependencies);

    internal bool TryGetNode(
        NavigationCellAddress address,
        TraversalMedium medium,
        out NavigationFlowFieldNode node)
    {
        int low = 0;
        int high = AddressLookupOrdinals.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            NavigationFlowFieldNode candidate = Nodes[AddressLookupOrdinals[middle]];
            int comparison = candidate.Address.CompareTo(address);
            if (comparison == 0)
                comparison = ((int)candidate.Medium).CompareTo((int)medium);
            if (comparison == 0)
            {
                node = candidate;
                return true;
            }
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        node = default;
        return false;
    }

    internal static long GetRetainedBytes(
        int nodeCount,
        int transitionInstructionCount,
        GraphDependencyStamp dependencies)
    {
        SwiftThrowHelper.ThrowIfNegative(nodeCount, nameof(nodeCount));
        SwiftThrowHelper.ThrowIfNegative(
            transitionInstructionCount,
            nameof(transitionInstructionCount));
        SwiftThrowHelper.ThrowIfNull(dependencies, nameof(dependencies));
        return checked(
            BaseRetainedBytes
            + GetArrayRetainedBytes(
                nodeCount,
                Unsafe.SizeOf<NavigationFlowFieldNode>())
            + GetArrayRetainedBytes(nodeCount, Int32Bytes)
            + GetArrayRetainedBytes(
                transitionInstructionCount,
                Unsafe.SizeOf<NavigationTransitionInstruction>())
            + dependencies.RetainedBytes);
    }

    internal static long GetMaximumRetainedBytes(
        int nodeCount,
        int transitionInstructionCount,
        int componentCount,
        int pageCount)
    {
        SwiftThrowHelper.ThrowIfNegative(nodeCount, nameof(nodeCount));
        SwiftThrowHelper.ThrowIfNegative(
            transitionInstructionCount,
            nameof(transitionInstructionCount));
        SwiftThrowHelper.ThrowIfNegative(componentCount, nameof(componentCount));
        SwiftThrowHelper.ThrowIfNegative(pageCount, nameof(pageCount));
        return checked(
            BaseRetainedBytes
            + GetArrayRetainedBytes(
                nodeCount,
                Unsafe.SizeOf<NavigationFlowFieldNode>())
            + GetArrayRetainedBytes(nodeCount, Int32Bytes)
            + GetArrayRetainedBytes(
                transitionInstructionCount,
                Unsafe.SizeOf<NavigationTransitionInstruction>())
            + GraphDependencyStamp.GetRetainedBytes(componentCount, pageCount));
    }

    private static long GetArrayRetainedBytes(int length, long elementBytes) =>
        length == 0
            ? 0L
            : Align8(checked(ArrayHeaderBytes + ((long)length * elementBytes)));

    private static long Align8(long value) => checked((value + 7L) & ~7L);
}
