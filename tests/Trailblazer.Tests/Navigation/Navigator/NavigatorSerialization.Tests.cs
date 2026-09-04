using System;
using System.Text.Json;
using Chronicler;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer.Heightmaps;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Pathing;
using Trailblazer.Tests.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public class NavigatorSerializationTests : IDisposable
{
    public NavigatorSerializationTests()
    {
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void JsonRoundTrip_ShouldRestoreMotorStateIntoExistingMotor()
    {
        var source = CreateConfiguredMotorAgent();
        NavMotor sourceMotor = TestRequire.NotNull(source.Motor);

        string json = JsonRecordSerializer.Serialize(sourceMotor, writeIndented: true);

        var target = MockMotorAgentTestFactory.CreateMockAgent(startPosition: new Vector3d(-2, 0, -2), startingMedium: TraversalMedium.Solid);
        NavMotor targetMotor = TestRequire.NotNull(target.Motor);
        JsonRecordSerializer.Populate(targetMotor, json);

        AssertMotorStateMatches(sourceMotor, targetMotor);
    }

    [Fact]
    public void JsonWire_ShouldPublishCurrentNavigatorSchemaAndGuidedStateKey()
    {
        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        ApplyGuidedRequest(source);

        string json = JsonRecordSerializer.Serialize(source);

        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("SchemaVersion").GetInt32().Should().Be(4);
        document.RootElement.GetProperty("PathSession").GetProperty("SchemaVersion").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("IsGuided").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldRestoreGroundSurfaceNormalIndependentlyFromPlatformTransform(
        bool useMemoryPack)
    {
        var source = CreateNavigator(Vector3d.Zero);
        var surfaceNormal = new Vector3d(
            Fixed64.Zero,
            Fixed64.FromFraction(4, 5),
            Fixed64.FromFraction(3, 5));
        source.SetGroundContact(
            surfaceLevel: Fixed64.Zero,
            surfaceNormal: surfaceNormal,
            platform: new PlatformSnapshot(7, Fixed4x4.Identity),
            updateMotorState: true);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        var target = CreateNavigator(new Vector3d(-3, 0, -3), profile: source.NavigationProfile);

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.FrameCondition.GroundState.Should().NotBeNull();
        GroundCondition ground = target.FrameCondition.GroundState.Value;
        ground.SurfaceNormal.Should().Be(surfaceNormal);
        ground.Platform.Transform.Up.Should().Be(Vector3d.Up);
        TestRequire.NotNull(target.Motor).CurrentState.SurfaceNormal.Should().Be(surfaceNormal);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldRejectMismatchedNavigationProfileWithoutMutatingShell(bool useMemoryPack)
    {
        var source = CreateNavigator(new Vector3d(4, 0, 4), size: (Fixed64)2);
        var target = CreateNavigator(new Vector3d(-3, 0, -3), size: Fixed64.One);
        NavigationAgentProfile shellProfile = target.NavigationProfile;
        Vector3d shellPosition = target.Position;
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);

        Action populate = () => SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        populate.Should().Throw<InvalidOperationException>();
        target.NavigationProfile.Should().Be(shellProfile);
        target.Position.Should().Be(shellPosition);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(false, 5)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    [InlineData(true, 5)]
#endif
    public void RoundTrip_ShouldRejectMissingOrInvalidFinalSchemaTransactionally(
        bool useMemoryPack,
        int invalidKind)
    {
        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = invalidKind switch
        {
            0 => SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "SchemaVersion"),
            1 => SerializationUtility.SetPayloadValue(payload, useMemoryPack, 99, "SchemaVersion"),
            2 => SerializationUtility.RemovePayloadEntry(
                payload,
                useMemoryPack,
                "PathSession",
                "SchemaVersion"),
            3 => SerializationUtility.SetPayloadValue(
                payload,
                useMemoryPack,
                99,
                "FrameCondition",
                "Medium"),
            4 => SerializationUtility.SetPayloadValue(payload, useMemoryPack, 3, "SchemaVersion"),
            5 => SerializationUtility.SetPayloadValue(
                payload,
                useMemoryPack,
                -Fixed64.One,
                "NavigationProfile",
                "Radius"),
            _ => throw new InvalidOperationException()
        };

        var target = CreateNavigator(new Vector3d(-3, 0, -3), profile: source.NavigationProfile);
        PathQuery shellQuery = new(
            new NavigationEndpoint(target.FootPosition),
            new NavigationEndpoint(new Vector3d(2, 0, 0)),
            target.NavigationProfile,
            new NavigationAreaPolicyKey("serialization-shell", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(8, 8, 8, 8, 8, 1, 1, 1, 1, 1, 1),
            allowTransitions: true);
        target.ApplyGuidedTrekRequest(shellQuery);
        Vector3d shellPosition = target.Position;

        Action populate = () => SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        populate.Should().Throw<InvalidOperationException>();
        target.Position.Should().Be(shellPosition);
        TestRequire.NotNull(target.Steering).CurrentQuery.Should().Be(shellQuery);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldRejectNegativePathBudgetWithoutChangingShellIntent(
        bool useMemoryPack)
    {
        var source = CreateNavigator(Vector3d.Zero);
        ApplyGuidedRequest(source);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = SerializationUtility.SetPayloadValue(
            payload,
            useMemoryPack,
            -1,
            "PathSession",
            "MaxLookupProbes");
        var target = CreateNavigator(new Vector3d(-3, 0, -3), profile: source.NavigationProfile);
        ApplyGuidedRequest(target);
        PathQuery shellQuery = TestRequire.NotNull(target.Steering).CurrentQuery!.Value;
        Vector3d shellPosition = target.Position;

        Action populate = () => SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        populate.Should().Throw<InvalidOperationException>();
        target.Position.Should().Be(shellPosition);
        TestRequire.NotNull(target.Steering).CurrentQuery.Should().Be(shellQuery);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
#endif
    public void RoundTrip_ShouldRejectEachInvalidPathSessionDiscriminatorTransactionally(
        bool useMemoryPack,
        int invalidKind)
    {
        var source = CreateNavigator(Vector3d.Zero);
        ApplyGuidedRequest(source);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = invalidKind switch
        {
            0 => SerializationUtility.SetPayloadValue(payload, useMemoryPack, 99, "PathSession", "Algorithm"),
            1 => SerializationUtility.SetPayloadValue(payload, useMemoryPack, 0, "PathSession", "TargetMedia"),
            2 => SerializationUtility.SetPayloadValue(payload, useMemoryPack, 8, "PathSession", "TargetMedia"),
            3 => SerializationUtility.SetPayloadValue(payload, useMemoryPack, " ", "PathSession", "AreaPolicyId"),
            _ => throw new InvalidOperationException()
        };
        var target = CreateNavigator(new Vector3d(-3, 0, -3), profile: source.NavigationProfile);
        PathQuery shellQuery = new(
            new NavigationEndpoint(target.FootPosition),
            new NavigationEndpoint(new Vector3d(2, 0, 0)),
            target.NavigationProfile,
            new NavigationAreaPolicyKey("serialization-shell", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(8, 8, 8, 8, 8, 1, 1, 1, 1, 1, 1),
            allowTransitions: true);
        target.ApplyGuidedTrekRequest(shellQuery);

        Action populate = () => SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        populate.Should().Throw<InvalidOperationException>();
        TestRequire.NotNull(target.Steering).CurrentQuery.Should().Be(shellQuery);
    }

    [Theory]
    [InlineData("Steering", "BrakingPower")]
    [InlineData("Steering", "WaypointTolerance")]
    [InlineData("Steering", "GroupFactor")]
    [InlineData("Steering", "AvoidFactor")]
    [InlineData("Turning", "TurnRate")]
    [InlineData("Motor", "MaxSlowSpeed")]
    [InlineData("Motor", "MaxModerateSpeed")]
    [InlineData("Motor", "MaxSidewaysSpeed")]
    [InlineData("Motor", "MaxBackwardsSpeed")]
    [InlineData("Motor", "MaxGroundAcceleration")]
    [InlineData("Motor", "MaxAirAcceleration")]
    public void JsonPopulate_ShouldRejectNegativeNestedControllerLimitsTransactionally(
        string component,
        string member)
    {
        var source = CreateNavigator(new Vector3d(4, 0, 4));
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack: false);
        string[] path = component == "Motor"
            ? new[] { "Motor", "Handler", "Move", member }
            : new[] { component, member };
        payload = SerializationUtility.SetPayloadValue(
            payload,
            useMemoryPack: false,
            -Fixed64.One,
            path);
        var target = CreateNavigator(
            new Vector3d(-3, 0, -3),
            profile: source.NavigationProfile);
        Vector3d shellPosition = target.Position;
        NavSteering shellSteering = TestRequire.NotNull(target.Steering);
        NavTurning shellTurning = TestRequire.NotNull(target.Turning);
        NavMotor shellMotor = TestRequire.NotNull(target.Motor);
        Fixed64 brakingPower = shellSteering.BrakingPower;
        Fixed64 waypointTolerance = shellSteering.WaypointTolerance;
        Fixed64 groupFactor = shellSteering.GroupFactor;
        Fixed64 avoidFactor = shellSteering.AvoidFactor;
        Fixed64 turnRate = shellTurning.TurnRate;
        MoveLocomotion move = shellMotor.Handler.Move;
        Fixed64 maxSlowSpeed = move.MaxSlowSpeed;
        Fixed64 maxModerateSpeed = move.MaxModerateSpeed;
        Fixed64 maxSidewaysSpeed = move.MaxSidewaysSpeed;
        Fixed64 maxBackwardsSpeed = move.MaxBackwardsSpeed;
        Fixed64 maxGroundAcceleration = move.MaxGroundAcceleration;
        Fixed64 maxAirAcceleration = move.MaxAirAcceleration;

        Action populate = () => SerializationUtility.PopulateRecord(
            target,
            payload,
            useMemoryPack: false);

        populate.Should().Throw<InvalidOperationException>();
        target.Position.Should().Be(shellPosition);
        shellSteering.BrakingPower.Should().Be(brakingPower);
        shellSteering.WaypointTolerance.Should().Be(waypointTolerance);
        shellSteering.GroupFactor.Should().Be(groupFactor);
        shellSteering.AvoidFactor.Should().Be(avoidFactor);
        shellTurning.TurnRate.Should().Be(turnRate);
        move.MaxSlowSpeed.Should().Be(maxSlowSpeed);
        move.MaxModerateSpeed.Should().Be(maxModerateSpeed);
        move.MaxSidewaysSpeed.Should().Be(maxSidewaysSpeed);
        move.MaxBackwardsSpeed.Should().Be(maxBackwardsSpeed);
        move.MaxGroundAcceleration.Should().Be(maxGroundAcceleration);
        move.MaxAirAcceleration.Should().Be(maxAirAcceleration);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldReadCurrentGuidedStateWireKey(bool useMemoryPack)
    {
        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        ApplyGuidedRequest(source);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        var target = CreateNavigator(new Vector3d(-3, 0, -3), profile: source.NavigationProfile);
        target.IsGuided.Should().BeFalse();

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.IsGuided.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true, false)]
    [InlineData(true, true)]
#endif
    public void Populate_ShouldChangeOccupancyTransactionally(
        bool useMemoryPack,
        bool malformed)
    {
        Vector3d loadedPosition = new(4, 0, 4);
        Vector3d shellPosition = new(-4, 0, -4);
        var source = CreateNavigator(loadedPosition);
        Guid loadedId = source.GlobalId;
        NavigationAgentProfile profile = source.NavigationProfile;
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        if (malformed)
        {
            payload = SerializationUtility.SetPayloadValue(
                payload,
                useMemoryPack,
                -Fixed64.One,
                "Motor",
                "Handler",
                "Move",
                "MaxFastSpeed");
        }
        source.Reset();

        var target = CreateNavigator(shellPosition, profile: profile);
        Guid shellId = target.GlobalId;
        (_, Voxel oldVoxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, shellPosition);
        (_, Voxel loadedVoxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, loadedPosition);
        shellId.Should().NotBe(loadedId);
        oldVoxel.OccupantCount.Should().Be(1);
        loadedVoxel.OccupantCount.Should().Be(0);

        Action populate = () => SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        if (malformed)
        {
            populate.Should().Throw<InvalidOperationException>();
            target.GlobalId.Should().Be(shellId);
            target.Position.Should().Be(shellPosition);
            oldVoxel.OccupantCount.Should().Be(1);
            loadedVoxel.OccupantCount.Should().Be(0);
            GridOccupantManager.GetOccupiedIndices(TestWorld.World, target)
                .Should().Equal(oldVoxel.WorldIndex);
            return;
        }

        populate.Should().NotThrow();
        target.GlobalId.Should().Be(loadedId);
        target.Position.Should().Be(loadedPosition);
        oldVoxel.OccupantCount.Should().Be(0);
        loadedVoxel.OccupantCount.Should().Be(1);
        GridOccupantManager.GetOccupiedIndices(TestWorld.World, target)
            .Should().Equal(loadedVoxel.WorldIndex);
    }

#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [Fact]
    public void MemoryPackRoundTrip_ShouldRestoreMotorStateIntoExistingMotor()
    {
        var source = CreateConfiguredMotorAgent();
        NavMotor sourceMotor = TestRequire.NotNull(source.Motor);

        byte[] data = MemoryPackRecordSerializer.Serialize(sourceMotor);

        var target = MockMotorAgentTestFactory.CreateMockAgent(startPosition: new Vector3d(-2, 0, -2), startingMedium: TraversalMedium.Solid);
        NavMotor targetMotor = TestRequire.NotNull(target.Motor);
        MemoryPackRecordSerializer.Populate(targetMotor, data);

        AssertMotorStateMatches(sourceMotor, targetMotor);
    }

#endif

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldRebuildTurningRuntimeState_OnLoad(bool useMemoryPack)
    {
        var source = CreateNavigator(new Vector3d(2, 0, 2));
        NavTurning sourceTurning = TestRequire.NotNull(source.Turning);
        sourceTurning.TurnRate = (Fixed64)0.35f;
        sourceTurning.RequestTurnDirection(source.Forward, Vector3d.Right, interpolation: Fixed64.Half);
        sourceTurning.TrySimulateTurn(
            source.Position,
            source.LastPosition,
            source.Forward,
            source.Rotation,
            out _).Should().BeTrue();

        sourceTurning.TargetReached.Should().BeFalse();
        sourceTurning.TargetRotation.Should().NotBe(FixedQuaternion.Identity);

        var target = CreateNavigator(new Vector3d(-4, 0, -4), profile: source.NavigationProfile);
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);
        NavTurning targetTurning = TestRequire.NotNull(target.Turning);

        targetTurning.CanTurn.Should().Be(sourceTurning.CanTurn);
        targetTurning.TurnRate.Should().Be(sourceTurning.TurnRate);
        targetTurning.TargetReached.Should().BeTrue();
        targetTurning.TargetRotation.Should().Be(FixedQuaternion.Identity);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldRestoreNavigatorHeightmapGroundingSettings(bool useMemoryPack)
    {
        var source = CreateNavigator(new Vector3d(2, 0, 2));
        source.ConfigureHeightmapGrounding(
            HeightmapGroundingMode.SurfaceLevelAndPosition,
            layerName: "Ground",
            groundOffset: Fixed64.Half,
            snapTolerance: (Fixed64)2);
        source.HeightmapGrounding.ActiveLayerName = "Platform";

        var target = CreateNavigator(new Vector3d(-4, 0, -4), profile: source.NavigationProfile);

        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);

        target.HeightmapGrounding.Mode.Should().Be(HeightmapGroundingMode.SurfaceLevelAndPosition);
        target.HeightmapGrounding.LayerName.Should().Be("Ground");
        target.HeightmapGrounding.ActiveLayerName.Should().Be("Platform");
        target.HeightmapGrounding.GroundOffset.Should().Be(Fixed64.Half);
        target.HeightmapGrounding.SnapTolerance.Should().Be((Fixed64)2);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldRejectNegativeHeightmapSnapToleranceWithoutChangingShell(
        bool useMemoryPack)
    {
        var source = CreateNavigator(new Vector3d(2, 0, 2));
        source.ConfigureHeightmapGrounding(
            HeightmapGroundingMode.SurfaceLevelAndPosition,
            layerName: "Source",
            snapTolerance: Fixed64.One);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = SerializationUtility.SetPayloadValue(
            payload,
            useMemoryPack,
            -Fixed64.One,
            "HeightmapGrounding",
            "SnapTolerance");
        var target = CreateNavigator(new Vector3d(-4, 0, -4), profile: source.NavigationProfile);
        target.ConfigureHeightmapGrounding(
            HeightmapGroundingMode.SurfaceLevelOnly,
            layerName: "Shell",
            snapTolerance: Fixed64.Half);
        Vector3d shellPosition = target.Position;

        Action populate = () => SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        populate.Should().Throw<ArgumentOutOfRangeException>();
        target.Position.Should().Be(shellPosition);
        target.HeightmapGrounding.Mode.Should().Be(HeightmapGroundingMode.SurfaceLevelOnly);
        target.HeightmapGrounding.LayerName.Should().Be("Shell");
        target.HeightmapGrounding.SnapTolerance.Should().Be(Fixed64.Half);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldRestoreHeightmapGroundingSettingsWithoutCreatingHeightmapData(bool useMemoryPack)
    {
        RegisterHeightmapSurface("MissingAfterLoad", height: 4, minSelectionY: Fixed64.Zero, maxSelectionY: (Fixed64)8);
        Fixed64 footOffset = PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY;
        var source = CreateNavigator(new Vector3d(Fixed64.Zero, (Fixed64)4 + footOffset, Fixed64.Zero));
        source.ConfigureHeightmapGrounding(HeightmapGroundingMode.SurfaceLevelAndPosition);
        source.ApplyHeightmapGrounding().Should().BeTrue();
        source.HeightmapGrounding.ActiveLayerName.Should().Be("MissingAfterLoad");

        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        TestWorld.Context.Reset();

        var target = CreateNavigator(
            new Vector3d(Fixed64.Zero, (Fixed64)4 + footOffset, Fixed64.Zero),
            profile: source.NavigationProfile);
        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.HeightmapGrounding.Mode.Should().Be(HeightmapGroundingMode.SurfaceLevelAndPosition);
        target.HeightmapGrounding.ActiveLayerName.Should().Be("MissingAfterLoad");
        target.ApplyHeightmapGrounding().Should().BeFalse();
        TestWorld.Context.Heightmaps.IsRegistered("MissingAfterLoad").Should().BeFalse();
    }

#if !TRAILBLAZER_DISABLE_MEMORYPACK
#endif

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldSupportPartialNavigatorPayloads_AndPreserveOmittedBranches(bool useMemoryPack)
    {
        var source = CreateConfiguredNavigator();
        source.OccupantGroupId = 9;
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);

        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "OccupantGroupId");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "Steering");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "Turning");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "Motor");

        var target = CreateNavigator(new Vector3d(-4, 0, -4), profile: source.NavigationProfile);
        target.OccupantGroupId = 9;
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);
        NavTurning targetTurning = TestRequire.NotNull(target.Turning);
        NavMotor targetMotor = TestRequire.NotNull(target.Motor);
        targetSteering.WaypointTolerance = (Fixed64)0.33f;
        targetTurning.TurnRate = (Fixed64)0.72f;
        targetMotor.Handler.Move.MaxFastSpeed = (Fixed64)8;

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.Position.Should().Be(source.Position);
        // since we removed the occupantGroupId entry, it should fall back to the default value of 1
        // regardless of the source and target values before population
        target.OccupantGroupId.Should().Be(1);
        targetSteering.WaypointTolerance.Should().Be((Fixed64)0.33f);
        targetTurning.TurnRate.Should().Be((Fixed64)0.72f);
        targetMotor.Handler.Move.MaxFastSpeed.Should().Be((Fixed64)8);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldPopulateDerivedNavigatorShellWithoutOptionalControllers(bool useMemoryPack)
    {
        var source = CreateNavigator(new Vector3d(3, 1, 2));
        source.ConfigurePartialControllerShell(
            includeSteering: false,
            includeTurning: false,
            includeMotor: false);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        var target = CreateNavigator(new Vector3d(-3, 0, -2), profile: source.NavigationProfile);
        target.ConfigurePartialControllerShell(
            includeSteering: false,
            includeTurning: false,
            includeMotor: false);

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.Position.Should().Be(source.Position);
        target.LastPosition.Should().Be(source.LastPosition);
        target.NavigationProfile.Should().Be(source.NavigationProfile);
        target.Steering.Should().BeNull();
        target.Turning.Should().BeNull();
        target.Motor.Should().BeNull();
        target.IsActive.Should().BeFalse();
        target.Invoking(navigator => navigator.Reset()).Should().NotThrow();
    }

    [Fact]
    public void JsonPopulate_ShouldRestoreDefaultHeightmapGroundingWhenNestedSettingsAreNull()
    {
        var source = CreateNavigator(new Vector3d(2, 0, 2));
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack: false);
        payload = SerializationUtility.SetPayloadValue<object?>(
            payload,
            useMemoryPack: false,
            value: null,
            "HeightmapGrounding");
        var target = CreateNavigator(new Vector3d(-2, 0, -2), profile: source.NavigationProfile);
        target.ConfigureHeightmapGrounding(
            HeightmapGroundingMode.SurfaceLevelAndPosition,
            layerName: "BeforeLoad",
            groundOffset: Fixed64.One,
            snapTolerance: Fixed64.One);

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack: false);

        target.HeightmapGrounding.Mode.Should().Be(HeightmapGroundingMode.Disabled);
        target.HeightmapGrounding.ActiveLayerName.Should().BeNull();
        target.HeightmapGrounding.GroundOffset.Should().Be(Fixed64.Zero);
        target.HeightmapGrounding.SnapTolerance.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldUseBackwardCompatibleDefaults_WhenPayloadOmitsFacingDirection(bool useMemoryPack)
    {
        var source = CreateConfiguredNavigator();
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);

        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "FrameRequest", "FacingDirection");

        var target = CreateNavigator(new Vector3d(-4, 0, -4), profile: source.NavigationProfile);
        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.FrameRequest.FacingDirection.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldUseBackwardCompatibleDefaults_WhenPayloadOmitsJumpAffordability(bool useMemoryPack)
    {
        var source = CreateConfiguredNavigator();
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);

        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "FrameRequest", "CanAffordJump");

        var target = CreateNavigator(new Vector3d(-4, 0, -4), profile: source.NavigationProfile);
        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.FrameRequest.CanAffordJump.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldClearTransientState_WhenLocomotionsLoadDisabled(bool useMemoryPack)
    {
        var source = CreateConfiguredMotorAgent();
        object payload = SerializationUtility.SerializeRecord(source.Motor, useMemoryPack);

        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Move", "IsEnabled");
        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Platform", "IsEnabled");
        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Jump", "IsEnabled");
        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Fall", "IsEnabled");
        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Slide", "IsEnabled");
        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Water", "IsEnabled");
        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Climb", "IsEnabled");

        var target = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: new Vector3d(-2, 0, -2),
            platformMatrix: MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(-1, 0, -1)),
            motionTransfer: MotionTransfer.PermaLocked);
        NavMotor targetMotor = TestRequire.NotNull(target.Motor);
        SerializationUtility.PopulateRecord(targetMotor, payload, useMemoryPack);
        var targetPlatform = TestRequire.NotNull(targetMotor.Handler.Platform);
        var targetJump = TestRequire.NotNull(targetMotor.Handler.Jump);
        var targetFall = TestRequire.NotNull(targetMotor.Handler.Fall);
        var targetSlide = TestRequire.NotNull(targetMotor.Handler.Slide);
        var targetWater = TestRequire.NotNull(targetMotor.Handler.Water);
        var targetClimb = TestRequire.NotNull(targetMotor.Handler.Climb);

        targetMotor.Handler.Move.IsEnabled.Should().BeFalse();
        targetMotor.Handler.Move.FrameVelocity.Should().Be(Vector3d.Zero);

        targetPlatform.IsEnabled.Should().BeFalse();
        targetPlatform.IsNewPlatform.Should().BeFalse();
        targetPlatform.ActivePlatform.Should().BeNull();
        targetPlatform.PreviousPlatform.Should().BeNull();
        targetPlatform.HoldPlatform.Should().BeNull();
        targetPlatform.MovementTransfer.Should().Be(MotionTransfer.None);
        targetPlatform.ScoutLocalPoint.Should().Be(Vector3d.Zero);
        targetPlatform.ScoutLocalRotation.Should().Be(FixedQuaternion.Identity);
        targetPlatform.PlatformVelocity.Should().Be(Vector3d.Zero);
        targetPlatform.FramePlatformVelocity.Should().Be(Vector3d.Zero);
        targetPlatform.HoldPlatformFrames.Should().Be(0);

        targetJump.IsEnabled.Should().BeFalse();
        targetJump.IsJumping.Should().BeFalse();
        targetJump.IsHoldingJump.Should().BeFalse();
        targetJump.JumpStartTime.Should().Be(Fixed64.Zero);
        targetJump.FrameJumpDirection.Should().Be(Vector3d.Zero);

        targetFall.IsEnabled.Should().BeFalse();
        targetFall.IsFalling.Should().BeFalse();
        targetFall.FallStart.Should().Be(Fixed64.Zero);
        targetFall.FallEnd.Should().Be(Fixed64.Zero);

        targetSlide.IsEnabled.Should().BeFalse();
        targetSlide.IsSliding.Should().BeFalse();

        targetWater.IsEnabled.Should().BeFalse();
        targetWater.IsSwimming.Should().BeFalse();
        targetWater.IsDiving.Should().BeFalse();
        targetWater.UnderwaterTimer.Should().Be(Fixed64.Zero);

        targetClimb.IsEnabled.Should().BeFalse();
        targetClimb.IsClimbing.Should().BeFalse();
        targetClimb.IsMantling.Should().BeFalse();
        targetClimb.AttachmentId.Should().BeNull();
    }

    private static TestNavigator CreateNavigator(
        Vector3d position,
        Fixed64? size = null,
        Fixed64? rootToFootOffsetY = null,
        NavigationAgentProfile? profile = null)
    {
        Fixed64 bodySize = size ?? (Fixed64)2;
        NavigationAgentProfile defaultProfile = PathTestFactory.DefaultNavigationProfile;
        NavigationAgentProfile navigationProfile = profile ?? new NavigationAgentProfile(
            new KinematicBodyShape(
                bodySize * Fixed64.Half,
                bodySize,
                rootToFootOffsetY ?? defaultProfile.Shape.RootToFootOffsetY),
            defaultProfile.MaxStepUp,
            defaultProfile.MaxDropDown,
            defaultProfile.ArrivalRadius,
            defaultProfile.AllowedMedia,
            defaultProfile.Capabilities);
        var navigator = new TestNavigator(TestWorld.Context);
        navigator.Setup(
            position,
            navigationProfile,
            rotation: FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.25f),
            velocity: new Vector3d(1, 0, 1));
        navigator.Initialize(new TrekCondition()
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });
        return navigator;
    }

    private static void ApplyGuidedRequest(TestNavigator navigator)
    {
        PathQuery query = new(
            new NavigationEndpoint(navigator.FootPosition),
            new NavigationEndpoint(new Vector3d(2, 0, 0)),
            navigator.NavigationProfile,
            new NavigationAreaPolicyKey("guided-wire", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(8, 8, 8, 8, 8, 1, 1, 1, 1, 1, 1),
            allowTransitions: true);
        navigator.ApplyGuidedTrekRequest(query);
    }

    private static MockMotorAgent CreateConfiguredMotorAgent()
    {
        var source = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: new Vector3d(2, 0, 3),
            platformMatrix: MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(4, 0, 5)),
            motionTransfer: MotionTransfer.PermaTransfer);
        NavMotor motor = TestRequire.NotNull(source.Motor);
        var jump = TestRequire.NotNull(motor.Handler.Jump);
        var fall = TestRequire.NotNull(motor.Handler.Fall);
        var slide = TestRequire.NotNull(motor.Handler.Slide);
        var water = TestRequire.NotNull(motor.Handler.Water);
        var fly = TestRequire.NotNull(motor.Handler.Fly);
        var platform = TestRequire.NotNull(motor.Handler.Platform);

        motor.Handler.IsInControl = false;
        motor.Handler.Move.MaxFastSpeed = (Fixed64)1.75f;
        motor.Handler.Move.FrameVelocity = new Vector3d(1, 2, 3);

        jump.MaxJumpCount = 2;
        jump.RegisterJump();
        jump.FrameJumpDirection = new Vector3d(0, 1, 1).Normalized;
        jump.StartCooldown();

        fall.IsFalling = true;
        fall.FallStart = (Fixed64)9;
        fall.FallEnd = (Fixed64)3;

        slide.IsSliding = true;

        water.IsSwimming = true;
        water.IsDiving = true;
        water.UnderwaterTimer = (Fixed64)7;

        fly.MaxFlySpeed = (Fixed64)2.5f;
        fly.GravityCompensation = (Fixed64)0.75f;
        fly.IsFlying = true;

        var holdPlatform = new PlatformSnapshot(9, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(6, 0, 6)));
        platform.IsNewPlatform = true;
        platform.PreviousPlatform = new PlatformSnapshot(8, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(3, 0, 3)));
        platform.SetHoldPlatform(holdPlatform);
        platform.TickHoldOnPlatform();
        platform.ScoutLocalPoint = new Vector3d(1, 0, 1);
        platform.ScoutLocalRotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.25f);
        platform.PlatformVelocity = new Vector3d(5, 0, 0);
        platform.FramePlatformVelocity = new Vector3d(2, 0, 0);

        return source;
    }

    private static TestNavigator CreateConfiguredNavigator()
    {
        var source = CreateNavigator(new Vector3d(2, 0, 2), rootToFootOffsetY: (Fixed64)0.75f);
        NavMotor motor = TestRequire.NotNull(source.Motor);
        NavTurning turning = TestRequire.NotNull(source.Turning);
        var platform = TestRequire.NotNull(motor.Handler.Platform);
        var jump = TestRequire.NotNull(motor.Handler.Jump);
        var fall = TestRequire.NotNull(motor.Handler.Fall);
        var fly = TestRequire.NotNull(motor.Handler.Fly);
        var climb = TestRequire.NotNull(motor.Handler.Climb);
        source.ApplyInputTrekRequest(
            Vector3d.Right,
            TrekRate.Moderate,
            isRequestingJump: true,
            facingDirection: Vector3d.Forward,
            canAffordJump: false);
        source.SetGroundContact(
            surfaceLevel: Fixed64.Zero,
            surfaceNormal: Vector3d.Up,
            platform: new PlatformSnapshot(12, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(1, 0, 1))),
            surfaceFriction: (Fixed64)0.15f,
            motionTransfer: MotionTransfer.PermaLocked,
            updateMotorState: true);

        source.IsLockedOn = true;
        motor.Handler.Move.FrameVelocity = new Vector3d(1, 0, 2);
        platform.ActivePlatform = new PlatformSnapshot(12, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(1, 0, 1)));
        platform.MovementTransfer = MotionTransfer.PermaLocked;
        platform.ScoutLocalPoint = new Vector3d(0, 0, 1);
        platform.ScoutLocalRotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.5f);
        jump.RegisterJump();
        jump.FrameJumpDirection = Vector3d.Up;
        fall.IsFalling = true;
        fall.FallStart = (Fixed64)10;
        fly.GravityCompensation = (Fixed64)0.8f;
        fly.IsFlying = true;
        climb.IsClimbing = true;
        climb.ActiveClimbKind = ClimbAffordanceKind.Surface;
        climb.AttachmentId = 21;
        climb.AttachmentPoint = new Vector3d(2, 1, 2);
        climb.AttachedSurfaceNormal = Vector3d.Left;
        climb.AttachedUpDirection = Vector3d.Up;
        turning.CanTurn = false;
        turning.TurnRate = (Fixed64)0.35f;

        return source;
    }

    private static void RegisterHeightmapSurface(
        string name,
        int height,
        Fixed64 minSelectionY,
        Fixed64 maxSelectionY)
    {
        HeightmapSurface surface = HeightmapSurface.FromHeights(
            name,
            new Fixed64[1, 1] { { (Fixed64)height } },
            Vector3d.Zero,
            Fixed64.One,
            new HeightmapCompression(Fixed64.Zero, Fixed64.One));

        TestWorld.Context.Heightmaps.Register(surface, minSelectionY, maxSelectionY).Should().BeTrue();
    }

    private static void AssertMotorStateMatches(NavMotor expected, NavMotor actual)
    {
        var expectedHandler = expected.Handler;
        var actualHandler = actual.Handler;
        var expectedJump = TestRequire.NotNull(expectedHandler.Jump);
        var actualJump = TestRequire.NotNull(actualHandler.Jump);
        var expectedFall = TestRequire.NotNull(expectedHandler.Fall);
        var actualFall = TestRequire.NotNull(actualHandler.Fall);
        var expectedSlide = TestRequire.NotNull(expectedHandler.Slide);
        var actualSlide = TestRequire.NotNull(actualHandler.Slide);
        var expectedWater = TestRequire.NotNull(expectedHandler.Water);
        var actualWater = TestRequire.NotNull(actualHandler.Water);
        var expectedFly = TestRequire.NotNull(expectedHandler.Fly);
        var actualFly = TestRequire.NotNull(actualHandler.Fly);
        var expectedClimb = TestRequire.NotNull(expectedHandler.Climb);
        var actualClimb = TestRequire.NotNull(actualHandler.Climb);
        var expectedPlatform = TestRequire.NotNull(expectedHandler.Platform);
        var actualPlatform = TestRequire.NotNull(actualHandler.Platform);

        actual.IsInitialized.Should().Be(expected.IsInitialized);
        actual.CurrentState.ToTrekCondition().Medium.Should().Be(expected.CurrentState.ToTrekCondition().Medium);
        actual.CurrentState.ToTrekCondition().SurfaceLevel.Should().Be(expected.CurrentState.ToTrekCondition().SurfaceLevel);
        actual.CurrentState.ToTrekCondition().CeilingLevel.Should().Be(expected.CurrentState.ToTrekCondition().CeilingLevel);
        actual.CurrentState.PreviousState.Should().Be(expected.CurrentState.PreviousState);

        actualHandler.IsInControl.Should().Be(expectedHandler.IsInControl);

        actualHandler.Move.IsEnabled.Should().Be(expectedHandler.Move.IsEnabled);
        actualHandler.Move.FrameVelocity.Should().Be(expectedHandler.Move.FrameVelocity);
        actualHandler.Move.MaxFastSpeed.Should().Be(expectedHandler.Move.MaxFastSpeed);

        actualJump.IsJumping.Should().Be(expectedJump.IsJumping);
        actualJump.IsHoldingJump.Should().Be(expectedJump.IsHoldingJump);
        actualJump.JumpStartTime.Should().Be(expectedJump.JumpStartTime);
        actualJump.FrameJumpDirection.Should().Be(expectedJump.FrameJumpDirection);
        actualJump.CanJump.Should().Be(expectedJump.CanJump);

        actualFall.IsFalling.Should().Be(expectedFall.IsFalling);
        actualFall.FallStart.Should().Be(expectedFall.FallStart);
        actualFall.FallEnd.Should().Be(expectedFall.FallEnd);

        actualSlide.IsSliding.Should().Be(expectedSlide.IsSliding);

        actualWater.IsSwimming.Should().Be(expectedWater.IsSwimming);
        actualWater.IsDiving.Should().Be(expectedWater.IsDiving);
        actualWater.UnderwaterTimer.Should().Be(expectedWater.UnderwaterTimer);

        actualFly.IsEnabled.Should().Be(expectedFly.IsEnabled);
        actualFly.MaxFlySpeed.Should().Be(expectedFly.MaxFlySpeed);
        actualFly.GravityCompensation.Should().Be(expectedFly.GravityCompensation);
        actualFly.IsFlying.Should().Be(expectedFly.IsFlying);

        actualClimb.IsEnabled.Should().Be(expectedClimb.IsEnabled);
        actualClimb.CanClimb.Should().Be(expectedClimb.CanClimb);
        actualClimb.IsClimbing.Should().Be(expectedClimb.IsClimbing);
        actualClimb.IsMantling.Should().Be(expectedClimb.IsMantling);
        actualClimb.ActiveClimbKind.Should().Be(expectedClimb.ActiveClimbKind);
        actualClimb.AttachmentId.Should().Be(expectedClimb.AttachmentId);
        actualClimb.AttachmentPoint.Should().Be(expectedClimb.AttachmentPoint);
        actualClimb.AttachedSurfaceNormal.Should().Be(expectedClimb.AttachedSurfaceNormal);
        actualClimb.AttachedUpDirection.Should().Be(expectedClimb.AttachedUpDirection);

        actualPlatform.IsNewPlatform.Should().Be(expectedPlatform.IsNewPlatform);
        actualPlatform.MovementTransfer.Should().Be(expectedPlatform.MovementTransfer);
        actualPlatform.ScoutLocalPoint.Should().Be(expectedPlatform.ScoutLocalPoint);
        actualPlatform.ScoutLocalRotation.Should().Be(expectedPlatform.ScoutLocalRotation);
        actualPlatform.PlatformVelocity.Should().Be(expectedPlatform.PlatformVelocity);
        actualPlatform.FramePlatformVelocity.Should().Be(expectedPlatform.FramePlatformVelocity);
        actualPlatform.HoldPlatformFrames.Should().Be(expectedPlatform.HoldPlatformFrames);

        var actualActivePlatform = TestRequire.NotNull(actualPlatform.ActivePlatform);
        var expectedActivePlatform = TestRequire.NotNull(expectedPlatform.ActivePlatform);
        actualActivePlatform.Id.Should().Be(expectedActivePlatform.Id);
        actualActivePlatform.Transform.Should().Be(expectedActivePlatform.Transform);

        actualPlatform.PreviousPlatform?.Id.Should().Be(expectedPlatform.PreviousPlatform?.Id);
        actualPlatform.PreviousPlatform?.Transform.Should().Be(expectedPlatform.PreviousPlatform?.Transform);
        actualPlatform.HoldPlatform?.Id.Should().Be(expectedPlatform.HoldPlatform?.Id);
        actualPlatform.HoldPlatform?.Transform.Should().Be(expectedPlatform.HoldPlatform?.Transform);
    }

    private static void AssertTurningStateMatches(NavTurning expected, NavTurning actual)
    {
        actual.CanTurn.Should().Be(expected.CanTurn);
        actual.TurnRate.Should().Be(expected.TurnRate);
        actual.TargetReached.Should().BeTrue();
        actual.TargetRotation.Should().Be(FixedQuaternion.Identity);
    }
}
