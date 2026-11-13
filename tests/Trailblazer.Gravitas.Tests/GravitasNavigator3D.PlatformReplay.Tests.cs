using FixedMathSharp;
using FluentAssertions;
using Gravitas.Support;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DPlatformReplayTests
{
    private readonly ITestOutputHelper _output;

    public GravitasNavigator3DPlatformReplayTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void ChangedPostureOnMovingPlatform_ShouldRestoreAndContinueTheExactTrace(bool memoryPack)
    {
        _output.WriteLine($"platform replay memoryPack={memoryPack}: platform-position|navigator-request|host-request|accepted|velocity|local-attachment");
        using var sourceFixture = new GravitasProfileReplacementScenarioFixture(addSupport: true);
        using GravitasNavigatorActor source = sourceFixture.CreateActor(carrierIdResolver: _ => 71);
        source.Body.Collider.IgnoredCollisionLayers = PhysicsLayerMask.None;
        sourceFixture.Step(source);
        NavigationAgentProfile standing = source.Navigator.NavigationProfile;
        var compact = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.Half, Fixed64.Quarter),
            standing.MaxStepUp, standing.MaxDropDown,
            standing.AllowedMedia, standing.Capabilities);
        source.Navigator.TryReplaceNavigationProfile(compact).Should().Be(NavigationProfileReplacementStatus.Applied);
        for (int frame = 0; frame < 8; frame++)
        {
            MovePlatform(sourceFixture, frame);
            sourceFixture.Step(source);
        }
        source.Navigator.Motor!.Handler.Platform.ActivePlatform!.Value.Id.Should().Be(71);
        object bodySnapshot = GravitasRecordSnapshot.Save(source.Body, memoryPack);
        object navigatorSnapshot = GravitasRecordSnapshot.Save(source.Navigator, memoryPack);

        using var restoredFixture = new GravitasProfileReplacementScenarioFixture(addSupport: true);
        // Publish the checkpoint platform pose during the last world-only step so
        // the first resumed CCD step sees the same committed collider as the source.
        for (int frame = 0; frame < 8; frame++)
            restoredFixture.Step();
        restoredFixture.SupportTransform!.LocalPosition = sourceFixture.SupportTransform!.LocalPosition;
        restoredFixture.SupportTransform.LocalRotation = sourceFixture.SupportTransform.LocalRotation;
        restoredFixture.Step();
        using GravitasNavigatorActor restored = restoredFixture.CreateActor(
            profile: compact, carrierIdResolver: _ => 71);
        GravitasRecordSnapshot.Load(restored.Body, bodySnapshot);
        GravitasRecordSnapshot.Load(restored.Navigator, navigatorSnapshot);
        restored.Navigator.NavigationProfile.Should().Be(compact);
        restored.Body.Position3d.Should().Be(source.Body.Position3d);
        restored.Body.Rotation.Should().Be(source.Body.Rotation);
        restored.Navigator.Rotation.Should().Be(source.Navigator.Rotation);
        restored.Navigator.Forward.Should().Be(source.Navigator.Forward);
        restored.Navigator.Turning!.TargetReached.Should().Be(source.Navigator.Turning!.TargetReached);
        restored.Navigator.Turning.TargetRotation.Should().Be(source.Navigator.Turning.TargetRotation);

        for (int frame = 8; frame < 40; frame++)
        {
            MovePlatform(sourceFixture, frame);
            MovePlatform(restoredFixture, frame);
            sourceFixture.Step(source);
            restoredFixture.Step(restored);
            restored.NavigatorRequestedPosition.Should().Be(source.NavigatorRequestedPosition, "replay frame {0}", frame);
            restored.Navigator.Position.Should().Be(source.Navigator.Position, "replay frame {0}", frame);
            restored.Navigator.Velocity.Should().Be(source.Navigator.Velocity, "replay frame {0}", frame);
            restored.Navigator.Rotation.Should().Be(source.Navigator.Rotation);
            restored.Body.Position3d.Should().Be(restored.Navigator.Position);
            restored.Navigator.Motor!.CurrentState.Medium.Should().Be(TraversalMedium.Solid);
            restored.Navigator.Motor.Handler.Platform.ActivePlatform!.Value.Id.Should().Be(71);
            var sourcePlatform = source.Navigator.Motor!.Handler.Platform;
            var restoredPlatform = restored.Navigator.Motor.Handler.Platform;
            restoredPlatform.ActivePlatform!.Value.Transform.Should().Be(sourcePlatform.ActivePlatform!.Value.Transform);
            restoredPlatform.ScoutLocalPoint.Should().Be(sourcePlatform.ScoutLocalPoint);
            restoredPlatform.ScoutLocalRotation.Should().Be(sourcePlatform.ScoutLocalRotation);
            GravitasMotionTrace.WriteFrame(_output, frame,
                restoredFixture.SupportTransform!.LocalPosition,
                restored.NavigatorRequestedPosition, restored.HostRequestedPosition,
                restored.Navigator.Position, restored.Navigator.Velocity, restoredPlatform.ScoutLocalPoint);
        }

        // Consume the last sampled platform transform, then check ordinary
        // frictional coasting without any displacement from the stationary carrier.
        for (int frame = 0; frame < 10; frame++)
        {
            if (frame == 2)
                GravitasRecordSnapshot.Load(restored.Navigator, GravitasRecordSnapshot.Save(source.Navigator, memoryPack));

            Vector3d coastingPosition = source.Navigator.Position
                + source.Navigator.Velocity * Fixed64.FromFraction(3, 4) * sourceFixture.Trailblazer.DeltaTime;
            sourceFixture.Step(source);
            restoredFixture.Step(restored);
            restored.NavigatorRequestedPosition.Should().Be(source.NavigatorRequestedPosition, "stationary platform frame {0}", frame);
            restored.Navigator.Position.Should().Be(source.Navigator.Position, "stationary platform frame {0}", frame);
            restored.Navigator.Velocity.Should().Be(source.Navigator.Velocity);
            restored.Navigator.Rotation.Should().Be(source.Navigator.Rotation);
            if (frame < 2)
                continue;

            source.NavigatorRequestedPosition.Should().Be(coastingPosition, "stationary platform frame {0}", frame);
        }
        source.Dispose();
        restored.Dispose();
        sourceFixture.Gravitas.Physics.BodyCount.Should().Be(0);
        restoredFixture.Gravitas.Physics.BodyCount.Should().Be(0);
        sourceFixture.Gravitas.Physics.ColliderCount.Should().Be(1, "only the host platform remains");
        restoredFixture.Gravitas.Physics.ColliderCount.Should().Be(1);
    }

    private static void MovePlatform(GravitasNavigatorScenarioFixture fixture, int frame)
    {
        fixture.SupportTransform!.LocalPosition = new Vector3d(
            Fixed64.One + (Fixed64)(frame + 1) / (Fixed64)32,
            Fixed64.FromFraction(-3, 4), Fixed64.Zero);
        fixture.SupportTransform.LocalRotation = FixedQuaternion.FromAxisAngle(
            Vector3d.Up, (Fixed64)(frame + 1) * Fixed64.Pi / (Fixed64)128);
    }
}
