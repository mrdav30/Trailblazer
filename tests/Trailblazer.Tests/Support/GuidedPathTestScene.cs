using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Tests;

internal static class GuidedPathTestScene
{
    internal static NavigationCell Cell(TraversalMedia media) => new(
        media,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.One,
        Fixed64.One);

    internal static Vector3d Anchor(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

    internal static NavigationGuideStep AdvanceToTransition(
        NavigationGuideLease lease)
    {
        for (int ordinal = 0; ordinal < lease.StepCount; ordinal++)
        {
            lease.TryGetCurrentStep(out NavigationGuideStep step)
                .Should().Be(NavigationGuideStatus.Success);
            if (step.HasTransition)
                return step;
            lease.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        }

        throw new InvalidOperationException(
            "The guide did not expose its authored transition.");
    }

    internal static void AdvanceUntilApplied(
        TrailblazerWorldContext context,
        params NavigationOperationReceipt[] receipts)
    {
        for (int frame = 0; frame < 1_024; frame++)
        {
            bool pending = false;
            for (int i = 0; i < receipts.Length; i++)
                pending |= receipts[i].Status == NavigationOperationStatus.Pending;
            if (!pending)
                break;
            context.Simulate();
        }

        for (int i = 0; i < receipts.Length; i++)
            receipts[i].Status.Should().Be(NavigationOperationStatus.Applied);
    }

    internal static void PublishMapAndPolicy(
        TrailblazerWorldContext context,
        NavigationMap map,
        int bakeVersion,
        OverlayReplacementPolicy replacementPolicy,
        long mapSequence,
        NavigationAreaPolicy policy,
        long policySequence)
    {
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, bakeVersion),
            replacementPolicy,
            mapSequence,
            effectiveFrame: context.FrameCount + 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            policy,
            policySequence,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(mapOperation).Should().BeTrue();
        context.Pathing.Admit(policyOperation).Should().BeTrue();
        AdvanceUntilApplied(context, mapOperation.Receipt, policyOperation.Receipt);
    }
}
