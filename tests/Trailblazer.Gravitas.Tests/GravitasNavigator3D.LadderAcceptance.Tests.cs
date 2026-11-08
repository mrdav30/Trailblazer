using FixedMathSharp;
using FluentAssertions;
using GridForge.Spatial;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DLadderAcceptanceTests
{
    private readonly ITestOutputHelper _output;

    public GravitasNavigator3DLadderAcceptanceTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Ladder_ShouldResolveTheClimbAndDismountBeforeCompletingOnce(PathAlgorithm algorithm)
    {
        _output.WriteLine($"ladder algorithm={algorithm}: navigator-request|host-request|accepted|velocity");
        using var fixture = new GravitasLadderScenarioFixture();
        using GravitasNavigatorActor actor = fixture.CreateActor();
        actor.Navigator.Motor!.Handler.Climb!.ClimbResolver = fixture;
        actor.Navigator.ApplyGuidedTrekRequest(fixture.Query(algorithm), TrekRate.Fast);
        int climbStarts = 0;
        actor.Navigator.Motor.Events.OnStartClimb += _ => climbStarts++;
        for (int frame = 0; frame < 512 && !actor.Navigator.PendingTransition.HasValue; frame++)
        {
            fixture.Step(actor);
            WriteFrame(actor, frame);
        }
        actor.Navigator.PendingTransition.Should().NotBeNull(
            "position {0}, requested {1}, host {2}, medium {3}, guided {4}",
            actor.Navigator.Position, actor.NavigatorRequestedPosition, actor.HostRequestedPosition,
            actor.Navigator.Motor.CurrentState.Medium, actor.Navigator.IsGuided);
        NavigationTransitionInstruction instruction = actor.Navigator.PendingTransition!.Value;
        instruction.Id.Should().Be(GravitasLadderScenarioFixture.TransitionId);
        for (int frame = fixture.ActionFrames; frame < GravitasLadderScenarioFixture.ActionFrameCount; frame++)
        {
            fixture.Step(actor);
            actor.Navigator.PendingTransition.Should().Be(instruction);
            actor.Navigator.Position.Should().Be(actor.Body.Position3d);
            WriteFrame(actor, frame);
        }

        fixture.ActionFrames.Should().Be(GravitasLadderScenarioFixture.ActionFrameCount);
        climbStarts.Should().Be(1);
        actor.Navigator.FootPosition.Should().Be(new Vector3d((Fixed64)2, Fixed64.FromFraction(3, 2), Fixed64.Zero));
        actor.Navigator.Motor.IsOnSolid.Should().BeTrue();
        actor.Navigator.CompletePendingTransition(instruction).Should().Be(NavigationGuideStatus.Success);
        actor.Navigator.CompletePendingTransition(instruction).Should().Be(NavigationGuideStatus.Stale);
        for (int frame = 0; frame < 512 && !actor.Navigator.Steering!.IsAtDestination; frame++)
            fixture.Step(actor);
        actor.Navigator.Steering!.IsAtDestination.Should().BeTrue();
        actor.Navigator.FootPosition.Y.Should().Be(Fixed64.FromFraction(3, 2));
        actor.Navigator.LastCommittedCell.Should().NotBeNull("arrival at {0}, foot {1}",
            actor.Navigator.Position, actor.Navigator.FootPosition);
        actor.Navigator.LastCommittedCell!.Value.Address.Should().Be(
            new NavigationCellAddress(GravitasLadderScenarioFixture.MapId, new VoxelIndex(5, 4, 2)));
    }

    [Fact]
    public void BlockedActionEntry_ShouldWaitForAcceptedMotionBeforeOfferingTheAction()
    {
        using var fixture = new GravitasLadderScenarioFixture();
        using GravitasNavigatorActor actor = fixture.CreateActor();
        var blocker = fixture.AddBox(new Vector3d(Fixed64.Half, Fixed64.Zero, Fixed64.Zero),
            new Vector3d(Fixed64.FromFraction(1, 32), (Fixed64)4, (Fixed64)2));
        actor.Navigator.Motor!.Handler.Climb!.ClimbResolver = fixture;
        actor.Navigator.ApplyGuidedTrekRequest(fixture.Query(PathAlgorithm.FlowField), TrekRate.Fast);
        for (int frame = 0; frame < 40; frame++)
        {
            fixture.Step(actor);
            actor.Navigator.PendingTransition.Should().BeNull();
            actor.Navigator.Position.Should().Be(actor.Body.Position3d);
            actor.Navigator.FootPosition.X.Should().BeLessThan(Fixed64.Half);
        }
        fixture.ActionFrames.Should().Be(0);

        blocker.Deactivate();
        // A stopped/stuck host may choose to retry the same destination after
        // the physical obstruction is removed; it never supplies an entry pose.
        PathQuery original = fixture.Query(PathAlgorithm.FlowField);
        var retry = new PathQuery(
            new NavigationEndpoint(actor.Navigator.FootPosition, GravitasLadderScenarioFixture.MapId),
            original.End, original.Agent, original.AreaPolicy,
            new TraversalIntent(actor.Navigator.Motor.CurrentState.Medium, original.Traversal.TargetMedia),
            original.Algorithm, original.Budget, original.AllowTransitions, original.FlowField);
        actor.Navigator.ApplyGuidedTrekRequest(retry, TrekRate.Fast);
        for (int frame = 0; frame < 512 && !actor.Navigator.PendingTransition.HasValue; frame++)
            fixture.Step(actor);
        actor.Navigator.PendingTransition.Should().NotBeNull();
        actor.Navigator.PendingTransition!.Value.Id.Should().Be(GravitasLadderScenarioFixture.TransitionId);
        actor.Dispose();
        fixture.Gravitas.Physics.BodyCount.Should().Be(0);

        // Fill every public lease slot after disposing a pending-action owner.
        // A leaked payload lease would prevent the last acquisition.
        var leases = new NavigationFlowFieldLease?[fixture.Trailblazer.Settings.QueryLimits.MaxFlowActivePayloadLeases];
        try
        {
            for (int i = 0; i < leases.Length; i++)
                fixture.Trailblazer.Guides.RequestFlowField(fixture.Query(PathAlgorithm.FlowField), out leases[i])
                    .Should().Be(NavigationGuideStatus.Success);
        }
        finally
        {
            foreach (NavigationFlowFieldLease? lease in leases)
                lease?.Dispose();
        }
    }

    [Fact]
    public void BlockedLadder_ShouldNotTreatRequestedHeightAsAnAcceptedActionDestination()
    {
        using var fixture = new GravitasLadderScenarioFixture(obstructClimb: true);
        using GravitasNavigatorActor actor = fixture.CreateActor();
        actor.Navigator.Motor!.Handler.Climb!.ClimbResolver = fixture;
        actor.Navigator.ApplyGuidedTrekRequest(fixture.Query(PathAlgorithm.AStar), TrekRate.Fast);
        for (int frame = 0; frame < 512 && fixture.ActionFrames < 2; frame++)
            fixture.Step(actor);
        fixture.ActionFrames.Should().Be(2);
        actor.Navigator.Position.Should().Be(actor.Body.Position3d);
        actor.Navigator.Position.Y.Should().BeLessThan(Fixed64.Half);
        actor.Navigator.PendingTransition.Should().NotBeNull();
        // Completion is a host assertion, not a physics query. The host must
        // withhold that assertion when accepted motion has not reached the exit.
        actor.Navigator.FootPosition.Should().NotBe(
            actor.Navigator.PendingTransition!.Value.DestinationPosition);
        actor.Navigator.Steering!.IsAtDestination.Should().BeFalse();
    }

    private void WriteFrame(GravitasNavigatorActor actor, int frame)
        => GravitasMotionTrace.WriteFrame(_output, frame,
            actor.NavigatorRequestedPosition, actor.HostRequestedPosition,
            actor.Navigator.Position, actor.Navigator.Velocity);
}
