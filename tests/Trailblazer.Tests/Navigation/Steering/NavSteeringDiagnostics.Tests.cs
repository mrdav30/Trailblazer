using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using SwiftCollections.Diagnostics;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Trailblazer.Tests.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Steering;

[Collection("TrailblazerLoggerCollection")]
public sealed class NavSteeringDiagnosticsTests : IDisposable
{
    private readonly DiagnosticLevel _originalMinimumLevel = TrailblazerLogger.MinimumLevel;
    private readonly bool _originalEnableDebugLogging = TrailblazerLogger.EnableDebugLogging;
    private readonly Action<DiagnosticLevel, string, string> _originalLogHandler = TrailblazerLogger.LogHandler;

    public NavSteeringDiagnosticsTests()
    {
        TestWorld.Setup();
    }

    public void Dispose()
    {
        TrailblazerLogger.MinimumLevel = _originalMinimumLevel;
        TrailblazerLogger.EnableDebugLogging = _originalEnableDebugLogging;
        TrailblazerLogger.LogHandler = _originalLogHandler;
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void DebugDiagnostics_ShouldDescribeControllerFailClosedAndTraversalStates()
    {
        var entries = new List<string>();
        TrailblazerLogger.MinimumLevel = DiagnosticLevel.Info;
        TrailblazerLogger.EnableDebugLogging = true;
        TrailblazerLogger.LogHandler = (_, message, _) => entries.Add(message);
        var vessel = new MockSteerAgent(Vector3d.Zero);

        var invalid = new DiagnosticSteering(DiagnosticSteeringMode.InvalidPath);
        invalid.ApplyPathQuery(CreateQuery());
        invalid.GetHeading(vessel, out _).Should().Be(Vector3d.Zero);
        invalid.IsAtDestination.Should().BeTrue();

        var noHeading = new DiagnosticSteering(DiagnosticSteeringMode.NoHeading);
        noHeading.ApplyPathQuery(CreateQuery());
        noHeading.GetHeading(vessel, out _);
        noHeading.IsAtDestination.Should().BeTrue();

        var stuck = new DiagnosticSteering(DiagnosticSteeringMode.Stuck);
        stuck.ApplyPathQuery(CreateQuery());
        for (int frame = 0; !stuck.IsAtDestination && frame < 64; frame++)
            stuck.GetHeading(vessel, out _);
        stuck.IsStuck.Should().BeTrue();
        stuck.IsAtDestination.Should().BeTrue();

        var motorAgent = MockMotorAgentTestFactory.CreateMockAgent(
            startingMedium: TraversalMedium.Solid,
            profile: LocomotionProfile.CreateCoreOnly());
        TestWorld.Context.Simulate();
        motorAgent.Simulate();

        entries.Should().Contain("Invalid path detected!");
        entries.Should().Contain("No viable movement direction found.");
        entries.Should().Contain("Stuck agent arriving!");
        entries.Should().Contain(message => message.StartsWith("NavMotor State:", StringComparison.Ordinal));
    }

    private static PathQuery CreateQuery() => new(
        new NavigationEndpoint(Vector3d.Zero),
        new NavigationEndpoint(new Vector3d(10, 0, 0)),
        PathTestFactory.DefaultNavigationProfile,
        new NavigationAreaPolicyKey("diagnostics", 1),
        new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
        PathAlgorithm.AStar,
        new NavigationWorkBudget(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
        allowTransitions: false);

    private enum DiagnosticSteeringMode
    {
        InvalidPath,
        NoHeading,
        Stuck
    }

    private sealed class DiagnosticSteering : NavSteering
    {
        private readonly DiagnosticSteeringMode _mode;

        public DiagnosticSteering(DiagnosticSteeringMode mode)
            : base(TestWorld.Context)
        {
            _mode = mode;
        }

        protected override bool ValidateMovementPath(Vector3d origin)
        {
            _shouldRequestPathThisFrame = false;
            _pathCheckCooldown = 2;
            return _mode != DiagnosticSteeringMode.InvalidPath;
        }

        protected override Vector3d FindTargetDirection(
            Vector3d position,
            out NavigationTransitionInstruction? pendingTransition)
        {
            if (_mode == DiagnosticSteeringMode.NoHeading)
                return base.FindTargetDirection(position, out pendingTransition);

            pendingTransition = null;
            _distanceToTarget = (Fixed64)10;
            return _mode == DiagnosticSteeringMode.Stuck
                ? Vector3d.Right
                : Vector3d.Zero;
        }
    }
}
