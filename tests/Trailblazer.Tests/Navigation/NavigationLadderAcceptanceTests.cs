using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class NavigationLadderAcceptanceTests
{
    private const string MapId = "hardening-ladder";
    private const string TransitionId = "ladder-up";
    private const int ClimbFrameCount = 4;

    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("hardening-ladder-policy", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    private static readonly NavigationWorkBudget Budget = new(
        8_192,
        128,
        1_024,
        8_192,
        1_024,
        1_024,
        1_024,
        128,
        1_024,
        1_024,
        1_024);

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void LadderRoute_ShouldApproachExecuteOnceAndContinueToTheUpperLanding(
        PathAlgorithm algorithm)
    {
        NavigationAgentProfile profile = CreateProfile(TraversalCapability.Climb);
        using LadderScenario scenario = LadderScenario.Create(profile);
        TestNavigator navigator = scenario.CreateNavigator();
        int climbStarts = 0;
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        motor.Events.OnStartClimb += _ => climbStarts++;
        motor.Handler.Climb!.ClimbResolver = new LadderAffordanceResolver(
            scenario.SourceRoot);
        navigator.ApplyGuidedTrekRequest(scenario.Query(algorithm), TrekRate.Moderate);

        navigator.Simulate();

        navigator.PendingTransition.Should().BeNull();
        navigator.FrameRequest.Direction.Should().Be(Vector3d.Right);
        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();

        navigator.CommitFrameMotion();
        navigator.Position.X.Should().BeGreaterThan(scenario.StartRoot.X);
        navigator.Position.X.Should().BeLessThan(scenario.SourceRoot.X);
        int approachFrames = SimulateUntilPendingTransition(
            scenario,
            navigator,
            maxFrames: 512);

        NavigationTransitionInstruction instruction = navigator.PendingTransition!.Value;
        navigator.ConstrainTestMotionToSurface(null);
        instruction.Id.Should().Be(TransitionId);
        instruction.Type.Should().Be(TraversalTransitionType.Climb);
        instruction.SourceMedium.Should().Be(TraversalMedium.Solid);
        instruction.DestinationMedium.Should().Be(TraversalMedium.Solid);
        instruction.LocomotionHints.Should().Be(
            TraversalTransitionLocomotionHints.RequestClimb);
        var hostAction = new FixedFrameLadderAction(
            instruction,
            scenario.SourceRoot,
            scenario.DestinationRoot,
            ClimbFrameCount);
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();
        motor.IsClimbing.Should().BeTrue();
        hostAction.ResolveNextFrame(navigator);
        navigator.CommitFrameMotion();
        navigator.Position.Should().Be(ExpectedClimbPosition(scenario, frame: 1));

        for (int frame = 2; frame <= ClimbFrameCount; frame++)
        {
            scenario.Context.Simulate();
            navigator.Simulate();

            navigator.PendingTransition.Should().Be(instruction);
            navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();
            motor.IsClimbing.Should().BeTrue();

            hostAction.ResolveNextFrame(navigator);
            navigator.CommitFrameMotion();
            navigator.Position.Should().Be(ExpectedClimbPosition(scenario, frame));
        }

        hostAction.Complete(navigator).Should().Be(NavigationGuideStatus.Success);
        hostAction.Complete(navigator).Should().Be(NavigationGuideStatus.Stale);
        navigator.ConstrainTestMotionToSurface(scenario.DestinationFoot.Y);
        navigator.SetGroundContact(
            scenario.DestinationFoot.Y,
            Vector3d.Up,
            updateMotorState: true);

        int continuationFrames = SimulateUntilArrival(
            scenario,
            navigator,
            maxFrames: 512);

        approachFrames.Should().BeInRange(2, 512);
        continuationFrames.Should().BeInRange(2, 512);
        climbStarts.Should().Be(1);
        (scenario.EndRoot - navigator.Position).Magnitude.Should()
            .BeLessThanOrEqualTo(Fixed64.FromFraction(1, 8));
        navigator.Position.Y.Should().Be(scenario.EndRoot.Y);
        navigator.IsGuided.Should().BeFalse();
        navigator.LastCommittedCell!.Value.Address.Should().Be(
            new NavigationCellAddress(MapId, LadderScenario.EndIndex));

        navigator.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void LadderRoute_WithoutClimbCapability_ShouldRejectBeforeHostAction(
        PathAlgorithm algorithm)
    {
        NavigationAgentProfile profile = CreateProfile(TraversalCapability.None);
        using LadderScenario scenario = LadderScenario.Create(profile);
        TestNavigator navigator = scenario.CreateNavigator();
        int invalidPathCount = 0;
        TestRequire.NotNull(navigator.Steering).Events.OnInvalidPath += () => invalidPathCount++;
        navigator.ApplyGuidedTrekRequest(scenario.Query(algorithm), TrekRate.Moderate);

        navigator.Simulate();

        invalidPathCount.Should().Be(1);
        navigator.IsGuided.Should().BeFalse();
        navigator.PendingTransition.Should().BeNull();
        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();
        navigator.Position.Should().Be(scenario.StartRoot);

        navigator.CommitFrameMotion();
        navigator.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void LadderRoute_InvalidatedWhileHeld_ShouldCancelBeforeCompletion(
        PathAlgorithm algorithm)
    {
        NavigationAgentProfile profile = CreateProfile(TraversalCapability.Climb);
        using LadderScenario scenario = LadderScenario.Create(profile);
        TestNavigator navigator = scenario.CreateNavigator();
        navigator.Motor!.Handler.Climb!.ClimbResolver = new LadderAffordanceResolver(
            scenario.SourceRoot);
        navigator.ApplyGuidedTrekRequest(scenario.Query(algorithm), TrekRate.Moderate);
        SimulateUntilPendingTransition(
                scenario,
                navigator,
                maxFrames: 512)
            .Should().BeInRange(1, 512);
        NavigationTransitionInstruction instruction = navigator.PendingTransition!.Value;
        navigator.ConstrainTestMotionToSurface(null);
        navigator.CommitFrameMotion();

        scenario.SuppressTransition();
        navigator.Simulate();

        navigator.PendingTransition.Should().BeNull();
        navigator.IsGuided.Should().BeFalse();
        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();
        navigator.Motor.IsClimbing.Should().BeFalse();
        navigator.CompletePendingTransition(instruction).Should().Be(NavigationGuideStatus.Stale);

        navigator.CommitFrameMotion();
        navigator.Reset();
    }

    private static int SimulateUntilPendingTransition(
        LadderScenario scenario,
        TestNavigator navigator,
        int maxFrames)
    {
        for (int frame = 1; frame <= maxFrames; frame++)
        {
            scenario.Context.Simulate();
            navigator.Simulate();
            if (navigator.PendingTransition.HasValue)
                return frame;
            navigator.CommitFrameMotion();
        }

        navigator.PendingTransition.Should().NotBeNull(
            "the guided actor must reach the ladder source within {0} fixed frames; final position was {1}",
            maxFrames,
            navigator.Position);
        return maxFrames;
    }

    private static int SimulateUntilArrival(
        LadderScenario scenario,
        TestNavigator navigator,
        int maxFrames)
    {
        for (int frame = 1; frame <= maxFrames; frame++)
        {
            scenario.Context.Simulate();
            navigator.Simulate();
            navigator.CommitFrameMotion();
            if (!navigator.IsGuided)
                return frame;
        }

        navigator.IsGuided.Should().BeFalse(
            "the actor must continue from the ladder destination to the upper landing within {0} fixed frames; final position was {1}",
            maxFrames,
            navigator.Position);
        return maxFrames;
    }

    private static Vector3d ExpectedClimbPosition(
        LadderScenario scenario,
        int frame) =>
        scenario.SourceRoot
        + Vector3d.Up
        * (scenario.DestinationRoot.Y - scenario.SourceRoot.Y)
        * (Fixed64)frame
        / (Fixed64)ClimbFrameCount;

    private static NavigationAgentProfile CreateProfile(
        TraversalCapability capabilities) => new(
        new KinematicBodyShape(
            Fixed64.One / (Fixed64)4,
            Fixed64.Half,
            Fixed64.One / (Fixed64)4),
        maxStepUp: Fixed64.Zero,
        maxDropDown: Fixed64.Zero,
        allowedMedia: TraversalMedia.Solid,
        capabilities);

    private sealed class LadderScenario : IDisposable
    {
        internal static readonly VoxelIndex StartIndex = new(0, 0, 0);
        internal static readonly VoxelIndex SourceIndex = new(1, 0, 0);
        internal static readonly VoxelIndex DestinationIndex = new(1, 2, 0);
        internal static readonly VoxelIndex EndIndex = new(2, 2, 0);

        private readonly NavigationAgentProfile _profile;
        private readonly NormalizedGridConfiguration _binding;

        private LadderScenario(
            TrailblazerWorldContext context,
            NavigationAgentProfile profile,
            NormalizedGridConfiguration binding)
        {
            Context = context;
            _profile = profile;
            _binding = binding;
            StartFoot = GuidedPathTestScene.Anchor(binding, StartIndex);
            SourceFoot = GuidedPathTestScene.Anchor(binding, SourceIndex);
            DestinationFoot = GuidedPathTestScene.Anchor(binding, DestinationIndex);
            EndFoot = GuidedPathTestScene.Anchor(binding, EndIndex);
        }

        internal TrailblazerWorldContext Context { get; }

        internal Vector3d StartFoot { get; }

        internal Vector3d SourceFoot { get; }

        internal Vector3d DestinationFoot { get; }

        internal Vector3d EndFoot { get; }

        internal Vector3d StartRoot => ToRoot(StartFoot);

        internal Vector3d SourceRoot => ToRoot(SourceFoot);

        internal Vector3d DestinationRoot => ToRoot(DestinationFoot);

        internal Vector3d EndRoot => ToRoot(EndFoot);

        internal static LadderScenario Create(NavigationAgentProfile profile)
        {
            TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
            var configuration = new GridConfiguration(
                Vector3d.Zero,
                new Vector3d(3, 3, 1),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
            context.World.TryAddGrid(configuration, out _).Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();

            NavigationCell walkable = GuidedPathTestScene.Cell(TraversalMedia.Solid);
            var ladder = new TraversalTransitionDefinition(
                TransitionId,
                TraversalTransitionType.Climb,
                SourceIndex,
                TraversalMedium.Solid,
                new NavigationCellAddress(MapId, DestinationIndex),
                TraversalMedium.Solid,
                TraversalCapability.Climb,
                locomotionHints: TraversalTransitionLocomotionHints.RequestClimb);
            NavigationMap map = new NavigationMapBuilder(MapId, binding)
                .AddCell(StartIndex, walkable)
                .AddCell(SourceIndex, walkable)
                .AddCell(DestinationIndex, walkable)
                .AddCell(EndIndex, walkable)
                .AddTransition(ladder)
                .Build();
            GuidedPathTestScene.PublishMapAndPolicy(
                context,
                map,
                bakeVersion: 1,
                OverlayReplacementPolicy.Clear,
                mapSequence: 1,
                Policy,
                policySequence: 2);

            return new LadderScenario(context, profile, binding);
        }

        internal TestNavigator CreateNavigator()
        {
            var navigator = new TestNavigator(Context);
            navigator.Setup(StartRoot, _profile);
            navigator.Initialize(new TrekCondition
            {
                Medium = TraversalMedium.Solid,
                SurfaceLevel = StartFoot.Y,
                GroundState = new GroundCondition
                {
                    SurfaceNormal = Vector3d.Up
                }
            });
            navigator.ConstrainTestMotionToSurface(StartFoot.Y);
            return navigator;
        }

        internal PathQuery Query(PathAlgorithm algorithm) => new(
            new NavigationEndpoint(StartFoot, MapId),
            new NavigationEndpoint(EndFoot, MapId),
            _profile,
            Policy.Key,
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            algorithm,
            Budget,
            allowTransitions: true, arrivalRadius: Fixed64.FromFraction(1, 8));

        internal void SuppressTransition()
        {
            var operation = new NavigationOverlayCommitOperation(
                new PreparedNavigationOverlay(
                    new NavigationOverlayTransaction(
                        new[]
                        {
                            new NavigationMapOverlayDelta(
                                MapId,
                                transitions: new[]
                                {
                                    TraversalTransitionOverlayOperation.Suppress(
                                        TransitionId)
                                })
                        })),
                operationSequence: 3,
                effectiveFrame: Context.FrameCount + 1);
            Context.Pathing.Admit(operation).Should().BeTrue();
            GuidedPathTestScene.AdvanceUntilApplied(Context, operation.Receipt);
        }

        public void Dispose() => Context.Dispose();

        private Vector3d ToRoot(Vector3d foot) =>
            foot + Vector3d.Up * _profile.Shape.RootToFootOffsetY;
    }

    private sealed class FixedFrameLadderAction
    {
        private readonly Vector3d _source;
        private readonly Vector3d _destination;
        private readonly int _frameCount;
        private readonly NavigationTransitionInstruction _instruction;
        private int _nextFrame;

        internal FixedFrameLadderAction(
            in NavigationTransitionInstruction instruction,
            Vector3d source,
            Vector3d destination,
            int frameCount)
        {
            _instruction = instruction;
            _source = source;
            _destination = destination;
            _frameCount = frameCount;
        }

        internal void ResolveNextFrame(TestNavigator navigator)
        {
            _nextFrame++;
            Fixed64 progress = (Fixed64)_nextFrame / (Fixed64)_frameCount;
            navigator.ResolveAcceptedFramePosition(
                Vector3d.Lerp(_source, _destination, progress));
        }

        internal NavigationGuideStatus Complete(TestNavigator navigator) =>
            navigator.CompletePendingTransition(_instruction);
    }

    private sealed class LadderAffordanceResolver : IClimbAffordanceResolver
    {
        private readonly Vector3d _attachmentPoint;

        internal LadderAffordanceResolver(Vector3d attachmentPoint)
        {
            _attachmentPoint = attachmentPoint;
        }

        public bool TryResolveClimbAffordance(
            TrekRequest request,
            TransitState currentState,
            out ClimbAffordanceSnapshot snapshot)
        {
            snapshot = new ClimbAffordanceSnapshot(
                ClimbAffordanceKind.Ladder,
                _attachmentPoint,
                Vector3d.Backward,
                Vector3d.Up,
                affordanceId: 41,
                allowLateralTraverse: false,
                allowDescent: false,
                allowMantle: false,
                allowDetachJump: false);
            return request.IsRequestingClimb
                && currentState.Medium == TraversalMedium.Solid;
        }
    }
}
