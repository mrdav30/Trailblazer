using Chronicler;
using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public sealed class ClimbLocomotionPhaseOneTests : IDisposable
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
            AllowLateralTraverse = false,
            IsClimbing = true,
            IsMantling = true,
            ActiveClimbKind = ClimbAffordanceKind.Ledge,
            AttachmentId = 12,
            AttachmentPoint = new Vector3d(3, 4, 5),
            AttachedSurfaceNormal = Vector3d.Left,
            AttachedUpDirection = Vector3d.Up
        };

        var target = new ClimbLocomotion();
        PopulateRecord(target, SerializeRecord(source, useMemoryPack), useMemoryPack);

        target.CanClimb.Should().BeFalse();
        target.MaxClimbSpeed.Should().Be((Fixed64)2);
        target.MaxClimbAcceleration.Should().Be((Fixed64)9);
        target.GravityCompensationWhileClimbing.Should().Be((Fixed64)0.75f);
        target.AllowLateralTraverse.Should().BeFalse();
        target.IsClimbing.Should().BeTrue();
        target.IsMantling.Should().BeTrue();
        target.ActiveClimbKind.Should().Be(ClimbAffordanceKind.Ledge);
        target.AttachmentId.Should().Be(12);
        target.AttachmentPoint.Should().Be(new Vector3d(3, 4, 5));
        target.AttachedSurfaceNormal.Should().Be(Vector3d.Left);
        target.AttachedUpDirection.Should().Be(Vector3d.Up);
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
            allowDetachJump: false);

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

    private static object SerializeRecord(IRecordable record, bool useMemoryPack)
    {
        return useMemoryPack
            ? MemoryPackRecordSerializer.Serialize(record)
            : JsonRecordSerializer.Serialize(record, writeIndented: true);
    }

    private static void PopulateRecord(IRecordable target, object payload, bool useMemoryPack)
    {
        if (useMemoryPack)
        {
            MemoryPackRecordSerializer.Populate(target, (byte[])payload);
            return;
        }

        JsonRecordSerializer.Populate(target, (string)payload);
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
}
