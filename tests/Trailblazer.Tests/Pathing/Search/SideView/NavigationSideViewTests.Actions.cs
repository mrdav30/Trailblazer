using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationSideViewTests
{
    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void AuthoredActionAfterStepUsesTheExactSupportedSource(PathAlgorithm algorithm, bool scheduled)
    {
        var scheduling = new NavigationSchedulingSettings(nodeStepLimit: 1, operationStepLimit: 1);
        using var context = Scene(out _, settings: PlanarSchedulingTestSettings.Create(scheduling));
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        AddFloor(builder, 1, -Fixed64.Quarter);
        builder.AddCell(Cell(3).Index, new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Half, Fixed64.One, Fixed64.Two));
        builder.AddPlanarSupport(Cell(3).Index, new((Fixed64)3 - Fixed64.Half, -Fixed64.Quarter),
            new((Fixed64)3 + Fixed64.Half, -Fixed64.Quarter));
        AddFloor(builder, 4, -Fixed64.Quarter);
        builder.AddTransition(new TraversalTransitionDefinition("jump", TraversalTransitionType.Jump,
            Cell(1).Index, TraversalMedium.Solid, Cell(3), TraversalMedium.Solid, TraversalCapability.Jump, (Fixed64)3));
        Publish(context, builder.Build());

        var profile = Profile(up: Fixed64.Quarter, capabilities: TraversalCapability.Jump);
        var budget = new NavigationWorkBudget(4096, 32, 64, 128, 0, 16, 0, 0, 0, 4096, 0);
        NavigationSideViewGuideLease acquired;
        if (scheduled)
        {
            context.Guides.ScheduleSideViewGuide(Cell(0), Cell(4), profile, Policy.Key, algorithm, budget,
                out var queued, allowTransitions: true).Should().Be(NavigationGuideStatus.Pending);
            using var request = TestRequire.NotNull(queued);
            int publicationNodes = 0;
            for (int frame = 0; frame < 128 && request.Status == NavigationGuideStatus.Pending; frame++)
            {
                bool publishing = request.Phase == NavigationGuideRequestPhase.Publication;
                TickScheduledSideView(context);
                if (publishing)
                    publicationNodes += context.Guides.Scheduler.LastFrameCountedWork.NodeRemaining;
            }
            request.Status.Should().Be(NavigationGuideStatus.Success);
            // Four anchors each for counting and writing. Flow also releases its four search nodes.
            publicationNodes.Should().Be(algorithm == PathAlgorithm.FlowField ? 12 : 8);
            request.TryTakeSideViewGuide(out acquired).Should().BeTrue();
        }
        else
        {
            context.Guides.RequestSideViewGuide(Cell(0), Cell(4), profile, Policy.Key, algorithm, budget, out acquired,
                allowTransitions: true).Should().Be(NavigationGuideStatus.Success);
        }
        using var guide = acquired;
        guide.StepCount.Should().Be(5);
        guide.TotalCost.Should().Be(Fixed64.FromFraction(23, 4));
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes
            .Should().Be(NavigationPlanarGuidePayload.MeasureBytes(5, 1));
        guide.TryGetCurrentStep(out var start, out var startCenter).Should().Be(NavigationGuideStatus.Success);
        start.Should().Be(Cell(0));
        startCenter.Should().Be(new Vector2d(Fixed64.Zero, -Fixed64.Quarter));
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        guide.TryGetCurrentStep(out var bendOwner, out var bend).Should().Be(NavigationGuideStatus.Success);
        bendOwner.Should().Be(Cell(0));
        bend.Should().Be(Vector2d.Zero);
        guide.TryGetPendingTransition(out _).Should().Be(NavigationGuideStatus.Pending);
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        guide.TryGetPendingTransition(out var action).Should().Be(NavigationGuideStatus.Success);
        action.Id.Should().Be("jump");
        action.SourceAddress.Should().Be(Cell(1));
        action.SourcePosition.Should().Be(Vector2d.Right);
        action.DestinationPosition.Should().Be(new Vector2d(3, 0));
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Pending);
        guide.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Success);
        guide.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Stale);
        guide.TryGetCurrentStep(out var destination, out var destinationCenter).Should().Be(NavigationGuideStatus.Success);
        destination.Should().Be(Cell(3));
        destinationCenter.Should().Be(new Vector2d(3, 0));
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        guide.TryGetCurrentStep(out var end, out var endCenter).Should().Be(NavigationGuideStatus.Success);
        end.Should().Be(Cell(4));
        endCenter.Should().Be(new Vector2d(4, 0));
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void AuthoredJumpRequiresOptInCapabilityAndClearActionLanding(PathAlgorithm algorithm)
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        AddFloor(builder, 1, -Fixed64.Half);
        AddFloor(builder, 3, -Fixed64.Half);
        AddFloor(builder, 4, -Fixed64.Half);
        builder.AddCell(Cell(0, 2).Index, Air).AddCell(Cell(1, 2).Index, Air).AddCell(Cell(4, 2).Index, Air);
        builder.AddTransition(new TraversalTransitionDefinition("jump", TraversalTransitionType.Jump,
            Cell(1).Index, TraversalMedium.Solid, Cell(3), TraversalMedium.Solid, TraversalCapability.Jump, (Fixed64)3));
        Publish(context, builder.Build());

        var budget = new NavigationWorkBudget(4096, 32, 64, 128, 0, 16, 0, 0, 0, 4096, 0);
        var compact = Profile(capabilities: TraversalCapability.Jump);
        var standing = Profile(Fixed64.Half + Fixed64.Quarter, capabilities: TraversalCapability.Jump);
        Anchor(context, Cell(0), standing, out _).Should().Be(NavigationGuideStatus.Success);
        Anchor(context, Cell(4), standing, out _).Should().Be(NavigationGuideStatus.Success);
        Anchor(context, Cell(3), standing, out _).Should().Be(NavigationGuideStatus.InvalidStart);
        Reject(Cell(0), Cell(4), standing, allowTransitions: true);
        Reject(Cell(0), Cell(4), Profile(), allowTransitions: true);
        Reject(Cell(0), Cell(4), compact, allowTransitions: false);
        Reject(Cell(4), Cell(0), compact, allowTransitions: true);

        context.Guides.RequestSideViewGuide(Cell(0), Cell(4), compact, Policy.Key, algorithm, budget, out var acquired,
            allowTransitions: true).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired;
        guide.TotalCost.Should().Be((Fixed64)5);

        void Reject(NavigationCellAddress from, NavigationCellAddress to, NavigationAgentProfile2D profile, bool allowTransitions)
        {
            context.Guides.RequestSideViewGuide(from, to, profile, Policy.Key, algorithm, budget, out var rejected,
                allowTransitions).Should().Be(NavigationGuideStatus.NoPath);
            rejected.StepCount.Should().Be(0);
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        }
    }
}
