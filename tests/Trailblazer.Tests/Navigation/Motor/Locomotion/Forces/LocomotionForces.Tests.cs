using FixedMathSharp;
using FixedMathSharp.Assertions;
using FluentAssertions;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class LocomotionForcesTests
{
    [Fact]
    public void Given_NoOverride_When_ReadingGravityForce_Then_ReturnsGlobal()
    {
        LocomotionForces loco = new();

        loco.HasGravityForceOverride.Should().BeFalse();
        loco.GravityForce.Should().Be(LocomotionForces.GlobalForces.GravityForce);
    }

    [Fact]
    public void Given_NoOverride_When_ReadingTerminalVelocity_Then_ReturnsGlobal()
    {
        LocomotionForces loco = new();

        loco.HasTerminalVelocityOverride.Should().BeFalse();
        loco.TerminalVelocity.Should().Be(LocomotionForces.GlobalForces.TerminalVelocity);
    }

    [Fact]
    public void Given_GlobalGravityChanged_When_NoOverride_Then_AllInstancesReflectNewGlobal()
    {
        Fixed64 newGravity = (Fixed64)2.0d;
        LocomotionForces.GlobalForces.GravityForce = newGravity;

        LocomotionForces loco = new();

        loco.GravityForce.Should().Be(newGravity);
        loco.HasGravityForceOverride.Should().BeFalse();
        LocomotionForces.GlobalForces.Reset();
    }

    [Fact]
    public void Given_PerInstanceOverride_When_GlobalChanges_Then_OverrideWins()
    {
        Fixed64 overrideGravity = (Fixed64)1.5d;
        LocomotionForces loco = new()
        {
            GravityForce = overrideGravity
        };

        LocomotionForces.GlobalForces.GravityForce = (Fixed64)20.0d;

        loco.GravityForce.Should().Be(overrideGravity);
        loco.HasGravityForceOverride.Should().BeTrue();
        LocomotionForces.GlobalForces.Reset();
    }

    [Fact]
    public void Given_PerInstanceOverride_When_Cleared_Then_FallsBackToGlobal()
    {
        Fixed64 newGlobal = (Fixed64)3.0d;
        LocomotionForces.GlobalForces.GravityForce = newGlobal;

        LocomotionForces loco = new()
        {
            GravityForce = (Fixed64)1.0d
        };
        loco.HasGravityForceOverride.Should().BeTrue();

        loco.ClearGravityForceOverride();

        loco.HasGravityForceOverride.Should().BeFalse();
        loco.GravityForce.Should().Be(newGlobal);
        LocomotionForces.GlobalForces.Reset();
    }

    [Fact]
    public void Given_PerInstanceTerminalVelocityOverride_When_Cleared_Then_FallsBackToGlobal()
    {
        Fixed64 newGlobal = (Fixed64)100.0d;
        LocomotionForces.GlobalForces.TerminalVelocity = newGlobal;

        LocomotionForces loco = new()
        {
            TerminalVelocity = (Fixed64)10.0d
        };
        loco.HasTerminalVelocityOverride.Should().BeTrue();
        loco.TerminalVelocity.Should().Be((Fixed64)10.0d);

        loco.ClearTerminalVelocityOverride();

        loco.HasTerminalVelocityOverride.Should().BeFalse();
        loco.TerminalVelocity.Should().Be(newGlobal);
        LocomotionForces.GlobalForces.Reset();
    }

    [Fact]
    public void Given_GlobalForcesReset_When_NoOverride_Then_GravityRestoresToDefault()
    {
        LocomotionForces.GlobalForces.GravityForce = (Fixed64)999.0d;
        LocomotionForces.GlobalForces.Reset();

        LocomotionForces loco = new();
        loco.GravityForce.Should().Be(GlobalEnvironmentForces.DefaultGravityForce);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void Serialization_ShouldPreservePerInstanceForceOverrides(bool useMemoryPack)
    {
        var source = new LocomotionForces
        {
            GravityForce = (Fixed64)2,
            TerminalVelocity = (Fixed64)17
        };
        var target = new LocomotionForces();

        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.HasGravityForceOverride.Should().BeTrue();
        target.GravityForce.Should().Be((Fixed64)2);
        target.HasTerminalVelocityOverride.Should().BeTrue();
        target.TerminalVelocity.Should().Be((Fixed64)17);
    }
}
