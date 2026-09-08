using System;
using Chronicler;
using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using SwiftCollections.Diagnostics;
using Trailblazer.Navigation.Motor;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DProfileReplacementTests
{
    [Fact]
    public void Replacement_ShouldRejectNonCapsuleBodiesWithoutMutatingEitherSystem()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(
            addWall: false);
        using GravitasNavigatorActor actor = fixture.CreateActor();
        NavigationAgentProfile original = actor.Navigator.NavigationProfile;
        Vector3d originalRoot = actor.Navigator.Position;
        LSCollider originalCollider = actor.Body.Collider;
        var replacement = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.Quarter,
                Fixed64.Half,
                Fixed64.Zero),
            original.MaxStepUp,
            original.MaxDropDown,
            original.ArrivalRadius,
            original.AllowedMedia,
            original.Capabilities);

        NavigationProfileReplacementStatus status =
            actor.Navigator.TryReplaceNavigationProfile(replacement);

        status.Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);
        actor.Navigator.NavigationProfile.Should().Be(original);
        actor.Navigator.Position.Should().Be(originalRoot);
        actor.Body.Position3d.Should().Be(originalRoot);
        actor.Body.Collider.Should().BeSameAs(originalCollider);
    }

    [Fact]
    public void Replacement_ShouldRejectCapsuleShapesThatCannotRepresentTheProfile()
    {
        using var fixture = new GravitasProfileReplacementScenarioFixture();
        using GravitasNavigatorActor actor = fixture.CreateActor();
        NavigationAgentProfile original = actor.Navigator.NavigationProfile;
        Vector3d originalRoot = actor.Navigator.Position;
        LSCollider originalCollider = actor.Body.Collider;
        var zeroRadius = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.Zero,
                Fixed64.One,
                Fixed64.Half),
            original.MaxStepUp,
            original.MaxDropDown,
            original.ArrivalRadius,
            original.AllowedMedia,
            original.Capabilities);
        var widerThanTall = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One,
                Fixed64.One,
                Fixed64.Half),
            original.MaxStepUp,
            original.MaxDropDown,
            original.ArrivalRadius,
            original.AllowedMedia,
            original.Capabilities);

        actor.Navigator.TryReplaceNavigationProfile(zeroRadius)
            .Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);
        actor.Navigator.TryReplaceNavigationProfile(widerThanTall)
            .Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);

        actor.Navigator.NavigationProfile.Should().Be(original);
        actor.Navigator.Position.Should().Be(originalRoot);
        actor.Body.Position3d.Should().Be(originalRoot);
        actor.Body.Collider.Should().BeSameAs(originalCollider);
    }

    [Fact]
    public void Replacement_ShouldApplyProfileOnlyChangesWhenTheCapsuleIsAlreadyExact()
    {
        using var fixture = new GravitasProfileReplacementScenarioFixture();
        using GravitasNavigatorActor actor = fixture.CreateActor();
        NavigationAgentProfile original = actor.Navigator.NavigationProfile;
        Vector3d originalRoot = actor.Navigator.Position;
        LSCollider originalCollider = actor.Body.Collider;
        var replacement = new NavigationAgentProfile(
            original.Shape,
            original.MaxStepUp,
            original.MaxDropDown,
            Fixed64.Quarter,
            original.AllowedMedia,
            original.Capabilities);

        NavigationProfileReplacementStatus status =
            actor.Navigator.TryReplaceNavigationProfile(replacement);

        status.Should().Be(NavigationProfileReplacementStatus.Applied);
        actor.Navigator.NavigationProfile.Should().Be(replacement);
        actor.Navigator.Position.Should().Be(originalRoot);
        actor.Body.Position3d.Should().Be(originalRoot);
        actor.Body.Collider.Should().BeSameAs(originalCollider);
    }

    [Fact]
    public void Replacement_ShouldRejectANonUnitPhysicalScaleWithoutChangingTheProfile()
    {
        using var fixture = new GravitasProfileReplacementScenarioFixture();
        using GravitasNavigatorActor actor = fixture.CreateActor();
        NavigationAgentProfile original = actor.Navigator.NavigationProfile;
        Vector3d originalRoot = actor.Navigator.Position;
        LSCollider originalCollider = actor.Body.Collider;
        Vector3d scaled = new((Fixed64)2, Fixed64.One, Fixed64.One);
        actor.Body.PositionTransform.LocalScale = scaled;
        var replacement = new NavigationAgentProfile(
            original.Shape,
            original.MaxStepUp,
            original.MaxDropDown,
            Fixed64.Quarter,
            original.AllowedMedia,
            original.Capabilities);

        NavigationProfileReplacementStatus status =
            actor.Navigator.TryReplaceNavigationProfile(replacement);

        status.Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);
        actor.Navigator.NavigationProfile.Should().Be(original);
        actor.Navigator.Position.Should().Be(originalRoot);
        actor.Body.Position3d.Should().Be(originalRoot);
        actor.Body.PositionTransform.LocalScale.Should().Be(scaled);
        actor.Body.Collider.Should().BeSameAs(originalCollider);
    }

    [Fact]
    public void Replacement_ShouldCommitTheExactCapsuleAndPreserveTheFootAnchor()
    {
        using var fixture = new GravitasProfileReplacementScenarioFixture(
            addSupport: true);
        using GravitasNavigatorActor actor = fixture.CreateActor();
        var crouching = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.Quarter,
                Fixed64.Half,
                Fixed64.Zero),
            actor.Navigator.NavigationProfile.MaxStepUp,
            actor.Navigator.NavigationProfile.MaxDropDown,
            actor.Navigator.NavigationProfile.ArrivalRadius,
            actor.Navigator.NavigationProfile.AllowedMedia,
            actor.Navigator.NavigationProfile.Capabilities);
        Vector3d foot = actor.Navigator.FootPosition;
        Guid identity = actor.Navigator.GlobalId;
        LSCapsuleCollider collider = actor.Body.Collider
            .Should().BeOfType<LSCapsuleCollider>().Which;

        NavigationProfileReplacementStatus status =
            actor.Navigator.TryReplaceNavigationProfile(crouching);

        status.Should().Be(NavigationProfileReplacementStatus.Applied);
        actor.Navigator.GlobalId.Should().Be(identity);
        actor.Navigator.FootPosition.Should().Be(foot);
        actor.Navigator.Position.Should().Be(foot);
        actor.Body.Position3d.Should().Be(actor.Navigator.Position);
        actor.Body.Collider.Should().BeSameAs(collider);
        collider.Radius.Should().Be(Fixed64.Quarter);
        collider.Size.Y.Should().Be(Fixed64.Half);
        collider.LocalOffset.Should().Be(Vector3d.Up * Fixed64.Quarter);
        collider.Center.Should().Be(foot + Vector3d.Up * Fixed64.Quarter);
    }

    [Fact]
    public void Replacement_WhenPhysicalExitCallbackThrows_ShouldCommitBothSystemsBeforeRethrowing()
    {
        using var fixture = new GravitasProfileReplacementScenarioFixture();
        using GravitasNavigatorActor actor = fixture.CreateActor();
        NavigationAgentProfile original = actor.Navigator.NavigationProfile;
        var crouching = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.Quarter,
                Fixed64.Half,
                Fixed64.Zero),
            original.MaxStepUp,
            original.MaxDropDown,
            original.ArrivalRadius,
            original.AllowedMedia,
            original.Capabilities);
        Vector3d foot = actor.Navigator.FootPosition;
        using GravitasNavigatorActor contactActor = fixture.CreateActor(
            position: actor.Navigator.Position
                + Vector3d.Right * Fixed64.FromFraction(3, 4));
        contactActor.Body.SetMotionType(BodyMotionType.Dynamic);
        fixture.Gravitas.Simulate();
        fixture.Gravitas.LateSimulate();
        int exits = 0;
        actor.Body.Collider.OnContactExit += _ =>
        {
            exits++;
            actor.Navigator.NavigationProfile.Should().Be(crouching);
            actor.Navigator.FootPosition.Should().Be(foot);
            actor.Body.Position3d.Should().Be(actor.Navigator.Position);
            SwiftThrowHelper.ThrowIfTrue(
                true,
                message: "adapter reconfiguration exit failure");
        };

        Action replace = () =>
            actor.Navigator.TryReplaceNavigationProfile(crouching);

        replace.Should().Throw<InvalidOperationException>()
            .WithMessage("adapter reconfiguration exit failure");
        exits.Should().Be(1);
        actor.Navigator.NavigationProfile.Should().Be(crouching);
        actor.Navigator.FootPosition.Should().Be(foot);
        actor.Body.Position3d.Should().Be(actor.Navigator.Position);
        LSCapsuleCollider collider = actor.Body.Collider
            .Should().BeOfType<LSCapsuleCollider>().Which;
        collider.Radius.Should().Be(crouching.Shape.Radius);
        collider.Size.Y.Should().Be(crouching.Shape.Height);
    }

    public static TheoryData<PathAlgorithm, bool> PostureCases => new()
    {
        { PathAlgorithm.AStar, false },
        { PathAlgorithm.FlowField, false },
        { PathAlgorithm.AStar, true },
        { PathAlgorithm.FlowField, true }
    };

    [Theory]
    [MemberData(nameof(PostureCases))]
    public void CompactPosture_ShouldTraverseLowPassageRejectGrowthAndExpandAfterExit(
        PathAlgorithm algorithm,
        bool ballPresentation)
    {
        using var fixture = new GravitasProfileReplacementScenarioFixture(
            addSupport: true);
        using GravitasNavigatorActor actor = fixture.CreateActor();
        NavigationAgentProfile standing = actor.Navigator.NavigationProfile;
        var compact = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.Quarter,
                Fixed64.Half,
                ballPresentation ? Fixed64.Quarter : Fixed64.Zero),
            standing.MaxStepUp,
            standing.MaxDropDown,
            Fixed64.FromFraction(1, 64),
            standing.AllowedMedia,
            standing.Capabilities);
        Vector3d passageCenter = (fixture.StartFoot + fixture.EndFoot) * Fixed64.Half
            + Vector3d.Up;
        fixture.AddCeiling(
            passageCenter,
            new Vector3d(Fixed64.One, Fixed64.Half, (Fixed64)2));
        Vector3d initialFoot = actor.Navigator.FootPosition;

        actor.Navigator.TryReplaceNavigationProfile(compact)
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        actor.Navigator.FootPosition.Should().Be(initialFoot);
        actor.Navigator.ApplyGuidedTrekRequest(
            fixture.CreateQueryForProfile(algorithm, compact),
            TrekRate.Fast);

        for (int frame = 0;
             frame < 256 && actor.Navigator.FootPosition.X < passageCenter.X;
             frame++)
        {
            fixture.Step(actor);
        }
        actor.Navigator.FootPosition.X.Should().BeGreaterThanOrEqualTo(passageCenter.X);
        actor.Navigator.FootPosition.Y.Should().Be(
            fixture.StartFoot.Y,
            "the compact posture should clear the passage without vertical displacement");
        Vector3d blockedFoot = actor.Navigator.FootPosition;
        PathQuery activeQuery = actor.Navigator.Steering!.CurrentQuery!.Value;

        actor.Navigator.TryReplaceNavigationProfile(standing)
            .Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);

        actor.Navigator.NavigationProfile.Should().Be(compact);
        actor.Navigator.FootPosition.Should().Be(blockedFoot);
        actor.Navigator.Steering.CurrentQuery.Should().Be(activeQuery);
        LSCapsuleCollider compactCollider = actor.Body.Collider
            .Should().BeOfType<LSCapsuleCollider>().Which;
        compactCollider.Radius.Should().Be(Fixed64.Quarter);
        compactCollider.Size.Y.Should().Be(Fixed64.Half);

        for (int frame = 0;
             frame < 256 && !actor.Navigator.Steering.IsAtDestination;
             frame++)
        {
            fixture.Step(actor);
            actor.Navigator.FootPosition.Y.Should().Be(
                fixture.StartFoot.Y,
                "frame {0} should keep compact traversal on the navigation floor; foot={1}, body={2}",
                frame,
                actor.Navigator.FootPosition,
                actor.Body.Position3d);
        }
        actor.Navigator.Steering.IsAtDestination.Should().BeTrue(
            "the compact actor should clear the passage; foot={0}, body={1}, medium={2}, velocity={3}",
            actor.Navigator.FootPosition,
            actor.Body.Position3d,
            actor.Navigator.Motor!.CurrentState.Medium,
            actor.Body.LinearVelocity);
        (actor.Navigator.FootPosition - fixture.EndFoot).Magnitude.Should()
            .BeLessThanOrEqualTo(compact.ArrivalRadius);
        Vector3d exitFoot = actor.Navigator.FootPosition;

        actor.Navigator.TryReplaceNavigationProfile(standing)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        actor.Navigator.FootPosition.Should().Be(exitFoot);
        actor.Navigator.Position.Should().Be(
            exitFoot + Vector3d.Up * standing.Shape.RootToFootOffsetY);
        actor.Body.Position3d.Should().Be(actor.Navigator.Position);
        compactCollider.Radius.Should().Be(standing.Shape.Radius);
        compactCollider.Size.Y.Should().Be(standing.Shape.Height);
        compactCollider.LocalOffset.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void CompactPostureRestore_ShouldLoadTheBodyBeforeNavigatorAndFinishThePassage()
    {
        using var fixture = new GravitasProfileReplacementScenarioFixture(
            addSupport: true);
        using GravitasNavigatorActor source = fixture.CreateActor();
        NavigationAgentProfile standing = source.Navigator.NavigationProfile;
        var compact = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.Quarter,
                Fixed64.Half,
                Fixed64.Zero),
            standing.MaxStepUp,
            standing.MaxDropDown,
            Fixed64.FromFraction(1, 64),
            standing.AllowedMedia,
            standing.Capabilities);
        Vector3d passageCenter = (fixture.StartFoot + fixture.EndFoot) * Fixed64.Half
            + Vector3d.Up;
        fixture.AddCeiling(
            passageCenter,
            new Vector3d(Fixed64.One, Fixed64.Half, (Fixed64)2));
        source.Navigator.TryReplaceNavigationProfile(compact)
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        source.Navigator.ApplyGuidedTrekRequest(
            fixture.CreateQueryForProfile(PathAlgorithm.AStar, compact),
            TrekRate.Fast);
        string bodyPayload = JsonRecordSerializer.Serialize(
            source.Body,
            writeIndented: true);
        string navigatorPayload = JsonRecordSerializer.Serialize(
            source.Navigator,
            writeIndented: true);
        source.Navigator.Reset();
        source.Body.Deactivate();

        using GravitasNavigatorActor restored = fixture.CreateActor(
            position: fixture.EndFoot
                + Vector3d.Up * compact.Shape.RootToFootOffsetY,
            profile: compact);
        JsonRecordSerializer.Populate(restored.Body, bodyPayload);
        JsonRecordSerializer.Populate(restored.Navigator, navigatorPayload);

        restored.Navigator.NavigationProfile.Should().Be(compact);
        restored.Navigator.FootPosition.Should().Be(fixture.StartFoot);
        restored.Body.Position3d.Should().Be(restored.Navigator.Position);
        LSCapsuleCollider collider = restored.Body.Collider
            .Should().BeOfType<LSCapsuleCollider>().Which;
        collider.Radius.Should().Be(compact.Shape.Radius);
        collider.Size.Y.Should().Be(compact.Shape.Height);
        collider.LocalOffset.Should().Be(Vector3d.Up
            * (compact.Shape.Height * Fixed64.Half
                - compact.Shape.RootToFootOffsetY));
        restored.Navigator.Steering!.CurrentQuery!.Value.Agent.Should().Be(compact);
        restored.Navigator.Steering.HasNavigationGuidance.Should().BeFalse();

        for (int frame = 0;
             frame < 512 && !restored.Navigator.Steering.IsAtDestination;
             frame++)
        {
            fixture.Step(restored);
        }

        restored.Navigator.Steering.IsAtDestination.Should().BeTrue();
        restored.Navigator.TryReplaceNavigationProfile(standing)
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        restored.Navigator.FootPosition.Should().Be(restored.Body.Position3d
            - Vector3d.Up * standing.Shape.RootToFootOffsetY);
        collider.Radius.Should().Be(standing.Shape.Radius);
        collider.Size.Y.Should().Be(standing.Shape.Height);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlatformPostureCycle_ShouldPreserveAttachmentAndCarry(
        bool ballPresentation)
    {
        using var fixture = new GravitasProfileReplacementScenarioFixture(
            addSupport: true);
        using GravitasNavigatorActor actor = fixture.CreateActor(
            carrierIdResolver: _ => 31);
        fixture.Step(actor);
        NavigationAgentProfile standing = actor.Navigator.NavigationProfile;
        var compact = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.Quarter,
                Fixed64.Half,
                ballPresentation ? Fixed64.Quarter : Fixed64.Zero),
            standing.MaxStepUp,
            standing.MaxDropDown,
            standing.ArrivalRadius,
            standing.AllowedMedia,
            standing.Capabilities);
        Vector3d initialFoot = actor.Navigator.FootPosition;
        PlatformSnapshot platform = actor.Navigator.Motor!.Handler.Platform
            .ActivePlatform.GetValueOrDefault();
        platform.Id.Should().Be(31);

        actor.Navigator.TryReplaceNavigationProfile(compact)
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        actor.Navigator.FootPosition.Should().Be(initialFoot);
        actor.Navigator.Motor.Handler.Platform.ActivePlatform.Should().Be(platform);

        fixture.SupportTransform!.LocalPosition += Vector3d.Right * Fixed64.Quarter;
        fixture.Step(actor);
        fixture.Step(actor);

        Vector3d carriedFoot = initialFoot + Vector3d.Right * Fixed64.Quarter;
        actor.Navigator.FootPosition.Should().Be(carriedFoot);
        actor.Body.Position3d.Should().Be(actor.Navigator.Position);
        actor.Navigator.Motor.Handler.Platform.ActivePlatform!.Value.Id.Should().Be(31);

        actor.Navigator.TryReplaceNavigationProfile(standing)
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        actor.Navigator.FootPosition.Should().Be(carriedFoot);
        actor.Body.Position3d.Should().Be(actor.Navigator.Position);
        actor.Navigator.Motor.Handler.Platform.ActivePlatform!.Value.Id.Should().Be(31);
    }
}
