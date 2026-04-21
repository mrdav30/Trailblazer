using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public sealed class ClimbLocomotionTests : IDisposable
{
    public void Dispose()
    {
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }


    [Fact]
    public void ClimbLocomotion_ShouldClearTransientState_WhenDisabled()
    {
        var locomotion = new ClimbLocomotion
        {
            IsClimbing = true,
            IsMantling = true,
            ActiveClimbKind = ClimbAffordanceKind.Surface,
            AttachmentId = 7,
            AttachmentPoint = new Vector3d(1, 2, 3),
            AttachedSurfaceNormal = Vector3d.Backward,
            AttachedUpDirection = Vector3d.Up
        };

        locomotion.IsEnabled = false;

        locomotion.IsClimbing.Should().BeFalse();
        locomotion.IsMantling.Should().BeFalse();
        locomotion.ActiveClimbKind.Should().Be(ClimbAffordanceKind.None);
        locomotion.AttachmentId.Should().BeNull();
        locomotion.AttachmentPoint.Should().Be(Vector3d.Zero);
        locomotion.AttachedSurfaceNormal.Should().Be(Vector3d.Zero);
        locomotion.AttachedUpDirection.Should().Be(Vector3d.Zero);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClimbLocomotion_Serialization_ShouldRoundTripRuntimeState(bool useMemoryPack)
    {
        var source = new ClimbLocomotion
        {
            CanClimb = false,
            MaxClimbSpeed = (Fixed64)2,
            MaxClimbAcceleration = (Fixed64)9,
            GravityCompensationWhileClimbing = (Fixed64)0.75f,
            ClimbStartTolerance = (Fixed64)0.25f,
            AllowLateralTraverse = false,
            ValidateActiveMantleWithHost = true,
            IsClimbing = true,
            IsMantling = true,
            ActiveClimbKind = ClimbAffordanceKind.Ledge,
            AttachmentId = 12,
            AttachmentPoint = new Vector3d(3, 4, 5),
            AttachedSurfaceNormal = Vector3d.Left,
            AttachedUpDirection = Vector3d.Up,
            ActiveAllowMantle = true,
            MantleTargetPosition = new Vector3d(4, 5, 6)
        };

        var target = new ClimbLocomotion();
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);

        target.CanClimb.Should().BeFalse();
        target.MaxClimbSpeed.Should().Be((Fixed64)2);
        target.MaxClimbAcceleration.Should().Be((Fixed64)9);
        target.GravityCompensationWhileClimbing.Should().Be((Fixed64)0.75f);
        target.ClimbStartTolerance.Should().Be((Fixed64)0.25f);
        target.AllowLateralTraverse.Should().BeFalse();
        target.ValidateActiveMantleWithHost.Should().BeTrue();
        target.IsClimbing.Should().BeTrue();
        target.IsMantling.Should().BeTrue();
        target.ActiveClimbKind.Should().Be(ClimbAffordanceKind.Ledge);
        target.AttachmentId.Should().Be(12);
        target.AttachmentPoint.Should().Be(new Vector3d(3, 4, 5));
        target.AttachedSurfaceNormal.Should().Be(Vector3d.Left);
        target.AttachedUpDirection.Should().Be(Vector3d.Up);
        target.ActiveAllowMantle.Should().BeTrue();
        target.MantleTargetPosition.Should().Be(new Vector3d(4, 5, 6));
    }

    [Fact]
    public void ClimbAffordanceSnapshot_Constructor_ShouldSetAllFields()
    {
        var snapshot = new ClimbAffordanceSnapshot(
            kind: ClimbAffordanceKind.Ladder,
            attachmentPoint: new Vector3d(1, 2, 3),
            surfaceNormal: Vector3d.Backward,
            upDirection: Vector3d.Up,
            affordanceId: 42,
            canStartClimb: false,
            canContinueClimb: true,
            allowLateralTraverse: false,
            allowDescent: false,
            allowMantle: true,
            allowDetachJump: false,
            mantleTargetPosition: new Vector3d(8, 9, 10));

        snapshot.Kind.Should().Be(ClimbAffordanceKind.Ladder);
        snapshot.AttachmentPoint.Should().Be(new Vector3d(1, 2, 3));
        snapshot.SurfaceNormal.Should().Be(Vector3d.Backward);
        snapshot.UpDirection.Should().Be(Vector3d.Up);
        snapshot.AffordanceId.Should().Be(42);
        snapshot.CanStartClimb.Should().BeFalse();
        snapshot.CanContinueClimb.Should().BeTrue();
        snapshot.AllowLateralTraverse.Should().BeFalse();
        snapshot.AllowDescent.Should().BeFalse();
        snapshot.AllowMantle.Should().BeTrue();
        snapshot.AllowDetachJump.Should().BeFalse();
        snapshot.MantleTargetPosition.Should().Be(new Vector3d(8, 9, 10));
    }

    [Fact]
    public void ClimbResolver_AndEvents_ShouldAllowHostOwnedPhaseOneWiring()
    {
        var motor = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid).Motor;
        var resolver = new StaticClimbResolver();
        bool started = false;
        bool stopped = false;
        bool mantled = false;
        bool slipped = false;

        motor.ClimbResolver = resolver;
        motor.Events.CanStartClimb = () => true;
        motor.Events.CanContinueClimb = () => true;
        motor.Events.OnStartClimb = _ => started = true;
        motor.Events.OnStopClimb = () => stopped = true;
        motor.Events.OnStartMantle = () => mantled = true;
        motor.Events.OnClimbSlip = () => slipped = true;

        bool resolved = motor.ClimbResolver!.TryResolveClimbAffordance(
            new TrekRequest { IsRequestingClimb = true },
            motor.CurrentState,
            out ClimbAffordanceSnapshot snapshot);

        resolved.Should().BeTrue();
        snapshot.Kind.Should().Be(ClimbAffordanceKind.Surface);
        motor.Events.CanStartClimb!.Invoke().Should().BeTrue();
        motor.Events.CanContinueClimb!.Invoke().Should().BeTrue();
        motor.Events.OnStartClimb!(snapshot);
        motor.Events.OnStopClimb!();
        motor.Events.OnStartMantle!();
        motor.Events.OnClimbSlip!();

        started.Should().BeTrue();
        stopped.Should().BeTrue();
        mantled.Should().BeTrue();
        slipped.Should().BeTrue();
    }

    [Fact]
    public void Given_LadderAffordance_When_ClimbRequested_Then_AttachesAndMovesUp()
    {
        var agent = CreateClimbingAgent();
        var resolver = new MutableClimbResolver
        {
            Snapshot = CreateLadderSnapshot()
        };
        int startedCount = 0;

        agent.Motor.ClimbResolver = resolver;
        agent.Motor.Events.OnStartClimb = _ => startedCount++;
        agent.Motor.Handler.Climb!.MaxClimbSpeed = (Fixed64)3;

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Up;
        agent.FrameRequest.Rate = TrekRate.Fast;
        agent.FrameRequest.IsRequestingClimb = true;
        agent.Simulate();

        agent.Motor.IsClimbing.Should().BeTrue();
        agent.Motor.IsFalling.Should().BeFalse();
        agent.Position.y.Should().BeGreaterThan(Fixed64.Zero);
        startedCount.Should().Be(1);
    }

    [Fact]
    public void Given_ActiveClimb_When_AffordanceRemainsValid_Then_ContinuesWithoutRestarting()
    {
        var agent = CreateClimbingAgent();
        var resolver = new MutableClimbResolver
        {
            Snapshot = CreateLadderSnapshot()
        };
        int startedCount = 0;

        agent.Motor.ClimbResolver = resolver;
        agent.Motor.Events.OnStartClimb = _ => startedCount++;
        agent.Motor.Handler.Climb!.MaxClimbSpeed = (Fixed64)3;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);
        Fixed64 firstHeight = agent.Position.y;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        agent.Motor.IsClimbing.Should().BeTrue();
        agent.Position.y.Should().BeGreaterThan(firstHeight);
        startedCount.Should().Be(1);
    }

    [Fact]
    public void Given_SurfaceAffordance_When_LateralTraverseAllowed_Then_MovesAcrossSurface()
    {
        var agent = CreateClimbingAgent();
        agent.Motor.ClimbResolver = new MutableClimbResolver
        {
            Snapshot = CreateSurfaceSnapshot(allowLateralTraverse: true)
        };
        agent.Motor.Handler.Climb!.MaxClimbSpeed = (Fixed64)3;

        SimulateClimbFrame(agent, Vector3d.Right, TrekRate.Fast);

        agent.Motor.IsClimbing.Should().BeTrue();
        agent.Position.x.Should().BeGreaterThan(Fixed64.Zero);
        agent.Position.y.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Given_SurfaceAffordance_When_LateralTraverseDisallowed_Then_DoesNotMoveSideways()
    {
        var agent = CreateClimbingAgent();
        agent.Motor.ClimbResolver = new MutableClimbResolver
        {
            Snapshot = CreateSurfaceSnapshot(allowLateralTraverse: false)
        };
        agent.Motor.Handler.Climb!.MaxClimbSpeed = (Fixed64)3;

        SimulateClimbFrame(agent, Vector3d.Right, TrekRate.Fast);

        agent.Motor.IsClimbing.Should().BeTrue();
        agent.Position.x.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Given_ContinuousSurfaceSnapshots_When_NormalsAndAttachmentShift_Then_ClimbContinues()
    {
        var agent = CreateClimbingAgent();
        var resolver = new MutableClimbResolver
        {
            Snapshot = CreateSurfaceSnapshot(allowLateralTraverse: true)
        };
        int startedCount = 0;
        agent.Motor.ClimbResolver = resolver;
        agent.Motor.Events.OnStartClimb = _ => startedCount++;
        agent.Motor.Handler.Climb!.MaxClimbSpeed = (Fixed64)3;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        resolver.Snapshot = CreateSurfaceSnapshot(
            allowLateralTraverse: true,
            attachmentPoint: new Vector3d(Fixed64.Zero, (Fixed64)0.25f, Fixed64.Zero),
            surfaceNormal: new Vector3d((Fixed64)0.25f, Fixed64.Zero, -Fixed64.One).Normal,
            upDirection: new Vector3d((Fixed64)0.1f, Fixed64.One, Fixed64.Zero).Normal,
            affordanceId: null);

        SimulateClimbFrame(agent, Vector3d.Up + Vector3d.Right, TrekRate.Fast);

        resolver.Snapshot = CreateSurfaceSnapshot(
            allowLateralTraverse: true,
            attachmentPoint: new Vector3d((Fixed64)0.2f, (Fixed64)0.45f, Fixed64.Zero),
            surfaceNormal: new Vector3d((Fixed64)0.4f, Fixed64.Zero, -Fixed64.One).Normal,
            upDirection: new Vector3d((Fixed64)0.15f, Fixed64.One, Fixed64.Zero).Normal,
            affordanceId: null);

        SimulateClimbFrame(agent, Vector3d.Up + Vector3d.Right, TrekRate.Fast);

        agent.Motor.IsClimbing.Should().BeTrue();
        agent.Position.y.Should().BeGreaterThan(Fixed64.Zero);
        agent.Position.x.Should().BeGreaterThan(Fixed64.Zero);
        startedCount.Should().Be(1);
    }

    [Fact]
    public void Given_LadderAffordance_When_DescentDisallowed_Then_DoesNotMoveDown()
    {
        var agent = CreateClimbingAgent(startPosition: new Vector3d(0, 2, 0));
        agent.Motor.ClimbResolver = new MutableClimbResolver
        {
            Snapshot = CreateLadderSnapshot(allowDescent: false)
        };
        agent.Motor.Handler.Climb!.MaxClimbSpeed = (Fixed64)3;

        SimulateClimbFrame(agent, Vector3d.Down, TrekRate.Fast);

        agent.Motor.IsClimbing.Should().BeTrue();
        agent.Position.y.Should().Be((Fixed64)2);
    }

    [Fact]
    public void Given_ContinuousSurfaceSnapshots_When_SurfaceFlipsAway_Then_SlipsAndStops()
    {
        var agent = CreateClimbingAgent();
        var resolver = new MutableClimbResolver
        {
            Snapshot = CreateSurfaceSnapshot(allowLateralTraverse: true, affordanceId: null)
        };
        int slipCount = 0;
        agent.Motor.ClimbResolver = resolver;
        agent.Motor.Events.OnClimbSlip = () => slipCount++;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        resolver.Snapshot = CreateSurfaceSnapshot(
            allowLateralTraverse: true,
            attachmentPoint: new Vector3d(Fixed64.Zero, (Fixed64)0.2f, Fixed64.Zero),
            surfaceNormal: Vector3d.Forward,
            upDirection: Vector3d.Up,
            affordanceId: null);

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        agent.Motor.IsClimbing.Should().BeFalse();
        slipCount.Should().Be(1);
    }

    [Fact]
    public void Given_HostStartVeto_When_ClimbRequested_Then_DoesNotAttach()
    {
        var agent = CreateClimbingAgent();
        agent.Motor.ClimbResolver = new MutableClimbResolver
        {
            Snapshot = CreateLadderSnapshot()
        };
        agent.Motor.Events.CanStartClimb = () => false;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        agent.Motor.IsClimbing.Should().BeFalse();
        agent.Position.y.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Given_ActiveClimb_When_HostContinueVetoes_Then_SlipsAndStops()
    {
        var agent = CreateClimbingAgent();
        agent.Motor.ClimbResolver = new MutableClimbResolver
        {
            Snapshot = CreateSurfaceSnapshot(allowLateralTraverse: true)
        };
        int slipCount = 0;
        agent.Motor.Events.OnClimbSlip = () => slipCount++;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);
        agent.Motor.Events.CanContinueClimb = () => false;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        agent.Motor.IsClimbing.Should().BeFalse();
        slipCount.Should().Be(1);
    }

    [Fact]
    public void Given_FallingAgent_When_ClimbAttaches_Then_GravityAndFallAreSuppressed()
    {
        var agent = MockMotorAgentTestFactory.CreateFallingAgent(startVelocity: new Vector3d(0, -4, 0));
        agent.Motor.ClimbResolver = new MutableClimbResolver
        {
            Snapshot = CreateLadderSnapshot()
        };

        TrailblazerManager.Simulate();
        agent.FrameRequest.Rate = TrekRate.Stationary;
        agent.FrameRequest.IsRequestingClimb = true;
        agent.Simulate();

        agent.Motor.IsClimbing.Should().BeTrue();
        agent.Motor.IsFalling.Should().BeFalse();
        agent.Position.y.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Given_ActiveClimb_When_RequestStopsInGas_Then_DetachesIntoFall()
    {
        var agent = CreateClimbingAgent();
        agent.Motor.ClimbResolver = new MutableClimbResolver
        {
            Snapshot = CreateSurfaceSnapshot(allowLateralTraverse: true)
        };

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Zero;
        agent.FrameRequest.Rate = TrekRate.Stationary;
        agent.FrameRequest.IsRequestingClimb = false;
        agent.Simulate();

        agent.Motor.IsClimbing.Should().BeFalse();
        agent.Motor.IsFalling.Should().BeTrue();
    }

    [Fact]
    public void Given_ActiveClimb_When_RequestStops_Then_DetachesAndRaisesStopEvent()
    {
        var agent = CreateClimbingAgent();
        agent.Motor.ClimbResolver = new MutableClimbResolver
        {
            Snapshot = CreateLadderSnapshot()
        };
        int stoppedCount = 0;
        int slippedCount = 0;
        agent.Motor.Events.OnStopClimb = () => stoppedCount++;
        agent.Motor.Events.OnClimbSlip = () => slippedCount++;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Up;
        agent.FrameRequest.Rate = TrekRate.Fast;
        agent.FrameRequest.IsRequestingClimb = false;
        agent.Simulate();

        agent.Motor.IsClimbing.Should().BeFalse();
        stoppedCount.Should().Be(1);
        slippedCount.Should().Be(0);
    }

    [Fact]
    public void Given_ActiveClimb_When_AffordanceDisappears_Then_SlipsAndStops()
    {
        var agent = CreateClimbingAgent();
        var resolver = new MutableClimbResolver
        {
            Snapshot = CreateLadderSnapshot()
        };
        int stoppedCount = 0;
        int slippedCount = 0;
        agent.Motor.ClimbResolver = resolver;
        agent.Motor.Events.OnStopClimb = () => stoppedCount++;
        agent.Motor.Events.OnClimbSlip = () => slippedCount++;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);
        resolver.Resolve = false;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        agent.Motor.IsClimbing.Should().BeFalse();
        stoppedCount.Should().Be(1);
        slippedCount.Should().Be(1);
    }

    [Fact]
    public void Given_LedgeAffordance_When_MovingUp_Then_StartsMantle()
    {
        var agent = CreateClimbingAgent();
        agent.Motor.ClimbResolver = new MutableClimbResolver
        {
            Snapshot = CreateLedgeSnapshot()
        };
        int mantleCount = 0;
        agent.Motor.Events.OnStartMantle = () => mantleCount++;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);
        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        agent.Motor.Handler.Climb!.IsMantling.Should().BeTrue();
        agent.Motor.Handler.Climb.MantleTargetPosition.Should().Be(new Vector3d(0, 2, 0));
        mantleCount.Should().Be(1);
    }

    [Fact]
    public void Given_ActiveMantle_When_HostTransitionsToSolid_Then_CompletesMantleAndStopsClimbing()
    {
        var agent = CreateClimbingAgent();
        agent.Motor.ClimbResolver = new MutableClimbResolver
        {
            Snapshot = CreateLedgeSnapshot()
        };

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);
        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        agent.FrameCondition.Medium = TraversalMedium.Solid;
        agent.FrameCondition.SurfaceLevel = agent.Position.y;
        agent.FrameCondition.GroundState = new GroundCondition();
        agent.Motor.SyncTraversalState(agent.FrameCondition);

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Zero;
        agent.FrameRequest.Rate = TrekRate.Stationary;
        agent.FrameRequest.IsRequestingClimb = false;
        agent.Simulate();

        agent.Motor.IsClimbing.Should().BeFalse();
        agent.Motor.Handler.Climb!.IsMantling.Should().BeFalse();
    }

    [Fact]
    public void Given_ActiveMantle_When_TraversalBecomesUnknown_Then_SlipsAndStops()
    {
        var agent = CreateClimbingAgent();
        agent.Motor.ClimbResolver = new MutableClimbResolver
        {
            Snapshot = CreateLedgeSnapshot()
        };
        int slipCount = 0;
        agent.Motor.Events.OnClimbSlip = () => slipCount++;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);
        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        agent.FrameCondition.Medium = TraversalMedium.Unknown;
        agent.FrameCondition.GroundState = null;

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Zero;
        agent.FrameRequest.Rate = TrekRate.Stationary;
        agent.FrameRequest.IsRequestingClimb = false;
        agent.Simulate();

        agent.Motor.IsClimbing.Should().BeFalse();
        slipCount.Should().Be(1);
    }

    [Fact]
    public void Given_ActiveMantleValidationDisabled_When_ValidatorWouldCancel_Then_DefaultMantlePathContinues()
    {
        var agent = CreateClimbingAgent();
        var resolver = new MantleValidatingClimbResolver
        {
            Snapshot = CreateLedgeSnapshot(),
            ValidationSnapshot = MantleValidationSnapshot.Cancel
        };
        int slipCount = 0;
        agent.Motor.ClimbResolver = resolver;
        agent.Motor.Events.OnClimbSlip = () => slipCount++;
        agent.Motor.Handler.Climb!.ValidateActiveMantleWithHost = false;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);
        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Zero;
        agent.FrameRequest.Rate = TrekRate.Stationary;
        agent.FrameRequest.IsRequestingClimb = false;
        agent.Simulate();

        agent.Motor.Handler.Climb!.IsMantling.Should().BeTrue();
        agent.Motor.IsClimbing.Should().BeTrue();
        resolver.ValidationCallCount.Should().Be(0);
        slipCount.Should().Be(0);
    }

    [Fact]
    public void Given_ActiveMantleValidationEnabled_When_ValidatorCancels_Then_SlipsAndStops()
    {
        var agent = CreateClimbingAgent();
        var resolver = new MantleValidatingClimbResolver
        {
            Snapshot = CreateLedgeSnapshot(),
            ValidationSnapshot = MantleValidationSnapshot.Cancel
        };
        int slipCount = 0;
        agent.Motor.ClimbResolver = resolver;
        agent.Motor.Events.OnClimbSlip = () => slipCount++;
        agent.Motor.Handler.Climb!.ValidateActiveMantleWithHost = true;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);
        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Zero;
        agent.FrameRequest.Rate = TrekRate.Stationary;
        agent.FrameRequest.IsRequestingClimb = false;
        agent.Simulate();

        agent.Motor.IsClimbing.Should().BeFalse();
        resolver.ValidationCallCount.Should().Be(1);
        slipCount.Should().Be(1);
    }

    [Fact]
    public void Given_ActiveMantleValidationEnabled_When_ValidatorAllows_Then_MantleContinues()
    {
        var agent = CreateClimbingAgent();
        var resolver = new MantleValidatingClimbResolver
        {
            Snapshot = CreateLedgeSnapshot(),
            ValidationSnapshot = MantleValidationSnapshot.Continue
        };
        agent.Motor.ClimbResolver = resolver;
        agent.Motor.Handler.Climb!.ValidateActiveMantleWithHost = true;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);
        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Zero;
        agent.FrameRequest.Rate = TrekRate.Stationary;
        agent.FrameRequest.IsRequestingClimb = false;
        agent.Simulate();

        agent.Motor.Handler.Climb!.IsMantling.Should().BeTrue();
        agent.Motor.IsClimbing.Should().BeTrue();
        resolver.ValidationCallCount.Should().Be(2);
        resolver.LastActiveMantle.Kind.Should().Be(ClimbAffordanceKind.Ledge);
        resolver.LastActiveMantle.AffordanceId.Should().Be(3);
        resolver.LastActiveMantle.AttachmentPoint.Should().Be(Vector3d.Zero);
        resolver.LastActiveMantle.SurfaceNormal.Should().Be(Vector3d.Backward);
        resolver.LastActiveMantle.UpDirection.Should().Be(Vector3d.Up);
        resolver.LastActiveMantle.MantleTargetPosition.Should().Be(new Vector3d(0, 2, 0));
    }

    [Fact]
    public void Given_ActiveMantleValidationEnabled_When_HostTransitionsToSolid_Then_CompletesMantleWithoutValidatorSlip()
    {
        var agent = CreateClimbingAgent();
        var resolver = new MantleValidatingClimbResolver
        {
            Snapshot = CreateLedgeSnapshot(),
            ValidationSnapshot = MantleValidationSnapshot.Continue
        };
        int slipCount = 0;
        agent.Motor.ClimbResolver = resolver;
        agent.Motor.Events.OnClimbSlip = () => slipCount++;
        agent.Motor.Handler.Climb!.ValidateActiveMantleWithHost = true;

        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);
        SimulateClimbFrame(agent, Vector3d.Up, TrekRate.Fast);

        agent.FrameCondition.Medium = TraversalMedium.Solid;
        agent.FrameCondition.SurfaceLevel = agent.Position.y;
        agent.FrameCondition.GroundState = new GroundCondition();
        agent.Motor.SyncTraversalState(agent.FrameCondition);

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Zero;
        agent.FrameRequest.Rate = TrekRate.Stationary;
        agent.FrameRequest.IsRequestingClimb = false;
        agent.Simulate();

        agent.Motor.IsClimbing.Should().BeFalse();
        agent.Motor.Handler.Climb!.IsMantling.Should().BeFalse();
        resolver.ValidationCallCount.Should().Be(1);
        slipCount.Should().Be(0);
    }

    [Fact]
    public void Given_LedgeAffordance_When_JumpRequested_Then_DetachesWithJumpImpulse()
    {
        var agent = CreateClimbingAgent();
        agent.Motor.ClimbResolver = new MutableClimbResolver
        {
            Snapshot = CreateLedgeSnapshot(allowDetachJump: true)
        };

        SimulateClimbFrame(agent, Vector3d.Zero, TrekRate.Fast);

        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = Vector3d.Zero;
        agent.FrameRequest.Rate = TrekRate.Fast;
        agent.FrameRequest.IsRequestingClimb = true;
        agent.FrameRequest.IsRequestingJump = true;
        agent.Simulate();

        agent.Motor.IsClimbing.Should().BeFalse();
        agent.Motor.IsJumping.Should().BeTrue();
        agent.Position.y.Should().BeGreaterThan(Fixed64.Zero);
    }

    private static MockMotorAgent CreateClimbingAgent(Vector3d? startPosition = null)
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: startPosition ?? Vector3d.Zero,
            startingMedium: TraversalMedium.Gas);
        agent.Motor.Handler.Climb.Should().NotBeNull();
        return agent;
    }

    private static void SimulateClimbFrame(MockMotorAgent agent, Vector3d direction, TrekRate rate)
    {
        TrailblazerManager.Simulate();
        agent.FrameRequest.Direction = direction;
        agent.FrameRequest.Rate = rate;
        agent.FrameRequest.IsRequestingClimb = true;
        agent.Simulate();
    }

    private static ClimbAffordanceSnapshot CreateLadderSnapshot(bool allowDescent = true)
    {
        return new ClimbAffordanceSnapshot(
            kind: ClimbAffordanceKind.Ladder,
            attachmentPoint: Vector3d.Zero,
            surfaceNormal: Vector3d.Backward,
            upDirection: Vector3d.Up,
            affordanceId: 1,
            allowLateralTraverse: false,
            allowDescent: allowDescent);
    }

    private static ClimbAffordanceSnapshot CreateSurfaceSnapshot(
        bool allowLateralTraverse,
        Vector3d? attachmentPoint = null,
        Vector3d? surfaceNormal = null,
        Vector3d? upDirection = null,
        int? affordanceId = 2)
    {
        return new ClimbAffordanceSnapshot(
            kind: ClimbAffordanceKind.Surface,
            attachmentPoint: attachmentPoint ?? Vector3d.Zero,
            surfaceNormal: surfaceNormal ?? Vector3d.Backward,
            upDirection: upDirection ?? Vector3d.Up,
            affordanceId: affordanceId,
            allowLateralTraverse: allowLateralTraverse,
            allowDescent: true);
    }

    private static ClimbAffordanceSnapshot CreateLedgeSnapshot(bool allowDetachJump = true)
    {
        return new ClimbAffordanceSnapshot(
            kind: ClimbAffordanceKind.Ledge,
            attachmentPoint: Vector3d.Zero,
            surfaceNormal: Vector3d.Backward,
            upDirection: Vector3d.Up,
            affordanceId: 3,
            allowLateralTraverse: false,
            allowDescent: false,
            allowMantle: true,
            allowDetachJump: allowDetachJump,
            mantleTargetPosition: new Vector3d(0, 2, 0));
    }

    private sealed class StaticClimbResolver : IClimbAffordanceResolver
    {
        public bool TryResolveClimbAffordance(TrekRequest request, TransitState currentState, out ClimbAffordanceSnapshot snapshot)
        {
            request.IsRequestingClimb.Should().BeTrue();
            currentState.Should().NotBeNull();
            snapshot = new ClimbAffordanceSnapshot(
                kind: ClimbAffordanceKind.Surface,
                attachmentPoint: new Vector3d(2, 3, 4),
                surfaceNormal: Vector3d.Left,
                upDirection: Vector3d.Up);
            return true;
        }
    }

    private sealed class MutableClimbResolver : IClimbAffordanceResolver
    {
        public bool Resolve = true;

        public ClimbAffordanceSnapshot Snapshot;

        public bool TryResolveClimbAffordance(TrekRequest request, TransitState currentState, out ClimbAffordanceSnapshot snapshot)
        {
            request.IsRequestingClimb.Should().BeTrue();
            currentState.Should().NotBeNull();
            snapshot = Snapshot;
            return Resolve;
        }
    }

    private sealed class MantleValidatingClimbResolver : IClimbAffordanceResolver, IActiveMantleValidator
    {
        public bool Resolve = true;

        public ClimbAffordanceSnapshot Snapshot;

        public bool ValidationResolve = true;

        public MantleValidationSnapshot ValidationSnapshot = MantleValidationSnapshot.Continue;

        public int ValidationCallCount;

        public ActiveMantleState LastActiveMantle;

        public bool TryResolveClimbAffordance(TrekRequest request, TransitState currentState, out ClimbAffordanceSnapshot snapshot)
        {
            request.IsRequestingClimb.Should().BeTrue();
            currentState.Should().NotBeNull();
            snapshot = Snapshot;
            return Resolve;
        }

        public bool TryValidateActiveMantle(
            TransitState currentState,
            ActiveMantleState activeMantle,
            out MantleValidationSnapshot snapshot)
        {
            currentState.Should().NotBeNull();
            ValidationCallCount++;
            LastActiveMantle = activeMantle;
            snapshot = ValidationSnapshot;
            return ValidationResolve;
        }
    }
}
