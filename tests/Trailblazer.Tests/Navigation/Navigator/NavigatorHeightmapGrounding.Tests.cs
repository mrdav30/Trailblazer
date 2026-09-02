using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer.Heightmaps;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class NavigatorHeightmapGroundingTests : IDisposable
{
    private static readonly Guid SharedOccupancyId =
        new("20000000-0000-0000-0000-000000000001");

    public NavigatorHeightmapGroundingTests()
    {
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryApplyHeightmapGrounding_Disabled_ShouldLeaveSurfaceLevelAndPositionUnchanged()
    {
        RegisterSurface("Ground", height: 4, Fixed64.Zero, (Fixed64)8);
        HeightmapTestNavigator navigator = CreateNavigator(new Vector3d(0, 1, 0), surfaceLevel: (Fixed64)7);

        navigator.ConfigureHeightmapGrounding(HeightmapGroundingMode.Disabled);

        navigator.ApplyHeightmapGrounding().Should().BeFalse();
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)7);
        navigator.Position.Should().Be(new Vector3d(0, 1, 0));
        navigator.HeightmapGrounding.ActiveLayerName.Should().BeNull();
    }

    [Fact]
    public void TryApplyHeightmapGrounding_InactiveShell_ShouldRemainANoOp()
    {
        var navigator = new HeightmapTestNavigator(TestWorld.Context);

        navigator.ApplyHeightmapGrounding().Should().BeFalse();

        navigator.IsActive.Should().BeFalse();
        navigator.Position.Should().Be(Vector3d.Zero);
        navigator.HeightmapGrounding.ActiveLayerName.Should().BeNull();
    }

    [Fact]
    public void ConfigureHeightmapGrounding_ShouldValidateAndNormalizeDeterministicSettings()
    {
        HeightmapTestNavigator navigator = CreateNavigator(new Vector3d(0, 1, 0));

        navigator.ConfigureHeightmapGrounding(
            HeightmapGroundingMode.SurfaceLevelOnly,
            layerName: "  ",
            groundOffset: Fixed64.One,
            snapTolerance: Fixed64.Half);

        navigator.HeightmapGrounding.LayerName.Should().BeNull();
        navigator.HeightmapGrounding.ActiveLayerName.Should().BeNull();
        navigator.HeightmapGrounding.GroundOffset.Should().Be(Fixed64.One);
        navigator.HeightmapGrounding.SnapTolerance.Should().Be(Fixed64.Half);

        Action negativeTolerance = () => navigator.ConfigureHeightmapGrounding(
            HeightmapGroundingMode.SurfaceLevelAndPosition,
            snapTolerance: -Fixed64.One);
        Action unknownMode = () => navigator.ConfigureHeightmapGrounding((HeightmapGroundingMode)99);

        negativeTolerance.Should().Throw<ArgumentOutOfRangeException>();
        unknownMode.Should().Throw<ArgumentOutOfRangeException>();

        navigator.ConfigureHeightmapGrounding(HeightmapGroundingMode.Disabled);
        navigator.HeightmapGrounding.ActiveLayerName.Should().BeNull();
        navigator.HeightmapGrounding.SnapTolerance.Should().BeNull();
    }

    [Fact]
    public void TryApplyHeightmapGrounding_SurfaceLevelOnly_ShouldUpdateGroundContactWithoutChangingRootY()
    {
        RegisterSurface("Ground", height: 3, Fixed64.Zero, (Fixed64)12);
        HeightmapTestNavigator navigator = CreateNavigator(new Vector3d(0, 10, 0), surfaceLevel: Fixed64.Zero);

        navigator.ConfigureHeightmapGrounding(HeightmapGroundingMode.SurfaceLevelOnly);

        navigator.ApplyHeightmapGrounding(updateMotorState: true, surfaceFriction: Fixed64.Half).Should().BeTrue();
        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Solid);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)3);
        navigator.Position.Y.Should().Be((Fixed64)10);
        navigator.FrameCondition.GroundState.Should().NotBeNull();
        navigator.FrameCondition.GroundState!.Value.SurfaceFriction.Should().Be(Fixed64.Half);
        navigator.Motor!.CurrentState.SurfaceLevel.Should().Be((Fixed64)3);
    }

    [Fact]
    public void TryApplyHeightmapGrounding_SurfaceLevelAndPosition_ShouldProjectRootToGroundFootAndOffset()
    {
        RegisterSurface("Ground", height: 3, -Fixed64.One, (Fixed64)8);
        HeightmapTestNavigator navigator = CreateNavigator(
            new Vector3d(0, 1, 0),
            surfaceLevel: Fixed64.Zero,
            rootToFootOffsetY: Fixed64.Half);

        navigator.ConfigureHeightmapGrounding(
            HeightmapGroundingMode.SurfaceLevelAndPosition,
            groundOffset: Fixed64.One);

        navigator.ApplyHeightmapGrounding().Should().BeTrue();
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)3);
        navigator.Position.Y.Should().Be((Fixed64)4 + Fixed64.Half);
    }

    [Fact]
    public void TryApplyHeightmapGrounding_SurfaceLevelAndPosition_ShouldShiftLastPositionWithRootProjection()
    {
        RegisterSurface("Ground", height: 5, Fixed64.Zero, (Fixed64)8);
        HeightmapTestNavigator navigator = CreateNavigator(new Vector3d(0, 1, 0), surfaceLevel: Fixed64.Zero);

        navigator.ApplyHeightmapGroundingAfterConfig(HeightmapGroundingMode.SurfaceLevelAndPosition)
            .Should().BeTrue();

        Fixed64 expectedRootY = (Fixed64)5 + PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY;
        navigator.Position.Y.Should().Be(expectedRootY);
        navigator.LastPosition.Y.Should().Be(expectedRootY);
    }

    [Fact]
    public void TryApplyHeightmapGrounding_WithinSameVoxel_ShouldPreserveSingleOccupancy()
    {
        Fixed64 footOffset = PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY;
        RegisterSurface("Ground", height: 0, -Fixed64.One, Fixed64.One);
        HeightmapTestNavigator navigator = CreateNavigator(
            new Vector3d(Fixed64.Zero, footOffset, Fixed64.Zero),
            surfaceLevel: Fixed64.Zero);
        (_, Voxel occupiedVoxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, navigator.Position);
        occupiedVoxel.OccupantCount.Should().Be(1);

        navigator.ConfigureHeightmapGrounding(
            HeightmapGroundingMode.SurfaceLevelAndPosition,
            groundOffset: Fixed64.Quarter);
        navigator.ApplyHeightmapGrounding().Should().BeTrue();

        navigator.Position.Y.Should().Be(Fixed64.Quarter + footOffset);
        occupiedVoxel.OccupantCount.Should().Be(1);
        GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator)
            .Should().Equal(occupiedVoxel.WorldIndex);
    }

    [Fact]
    public void TryApplyHeightmapGrounding_RootProjection_ShouldTransferVoxelOccupancy()
    {
        RegisterSurface("Ground", height: 3, (Fixed64)(-4), (Fixed64)4);
        Vector3d oldPosition = new(0, -3, 0);
        HeightmapTestNavigator navigator = CreateNavigator(oldPosition, surfaceLevel: Fixed64.Zero);
        (_, Voxel oldVoxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, oldPosition);
        oldVoxel.OccupantCount.Should().Be(1);

        navigator.ApplyHeightmapGroundingAfterConfig(HeightmapGroundingMode.SurfaceLevelAndPosition)
            .Should().BeTrue();

        (_, Voxel projectedVoxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, navigator.Position);
        projectedVoxel.Should().NotBeSameAs(oldVoxel);
        oldVoxel.OccupantCount.Should().Be(0);
        projectedVoxel.OccupantCount.Should().Be(1);
        GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator)
            .Should().Equal(projectedVoxel.WorldIndex);
    }

    [Fact]
    public void TryApplyHeightmapGrounding_RootProjectionOutsideWorld_ShouldRemoveOldOccupancy()
    {
        RegisterSurface("HighGround", height: 10, -Fixed64.One, Fixed64.One);
        Vector3d oldPosition = Vector3d.Zero;
        HeightmapTestNavigator navigator = CreateNavigator(oldPosition);
        (_, Voxel oldVoxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, oldPosition);
        oldVoxel.OccupantCount.Should().Be(1);

        navigator.ApplyHeightmapGroundingAfterConfig(HeightmapGroundingMode.SurfaceLevelAndPosition)
            .Should().BeTrue();

        navigator.Position.Y.Should().Be(
            (Fixed64)10 + PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY);
        oldVoxel.OccupantCount.Should().Be(0);
        GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator).Should().BeEmpty();
    }

    [Fact]
    public void TryApplyHeightmapGrounding_RootProjectionIntoWorld_ShouldRegisterNewOccupancy()
    {
        RegisterSurface("Ground", height: 0, (Fixed64)9, (Fixed64)11);
        Vector3d outsidePosition = new(Fixed64.Zero, (Fixed64)10, Fixed64.Zero);
        HeightmapTestNavigator navigator = CreateNavigator(outsidePosition);
        GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator).Should().BeEmpty();

        navigator.ApplyHeightmapGroundingAfterConfig(HeightmapGroundingMode.SurfaceLevelAndPosition)
            .Should().BeTrue();

        (_, Voxel newVoxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, navigator.Position);
        navigator.Position.Y.Should().Be(
            PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY);
        newVoxel.OccupantCount.Should().Be(1);
        GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator)
            .Should().Equal(newVoxel.WorldIndex);
    }

    [Fact]
    public void TryApplyHeightmapGrounding_DuplicateStableId_ShouldPreserveExistingOccupancy()
    {
        RegisterSurface("Ground", height: 0, (Fixed64)(-4), (Fixed64)4);
        Guid sharedId = SharedOccupancyId;
        Fixed64 footOffset = PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY;
        HeightmapTestNavigator incumbent = CreateNavigator(
            new Vector3d(Fixed64.Zero, footOffset, Fixed64.Zero),
            globalId: sharedId);
        HeightmapTestNavigator duplicate = CreateNavigator(
            new Vector3d(Fixed64.Zero, (Fixed64)3 + footOffset, Fixed64.Zero),
            globalId: sharedId);
        (_, Voxel incumbentVoxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, incumbent.Position);

        duplicate.ApplyHeightmapGroundingAfterConfig(HeightmapGroundingMode.SurfaceLevelAndPosition)
            .Should().BeTrue();

        incumbentVoxel.OccupantCount.Should().Be(1);
        GridOccupantManager.GetOccupiedIndices(TestWorld.World, incumbent)
            .Should().Equal(incumbentVoxel.WorldIndex);
        GridOccupantManager.GetOccupiedIndices(TestWorld.World, duplicate).Should().BeEmpty();
    }

    [Fact]
    public void TryApplyHeightmapGrounding_ShouldSkipProjection_WhenCurrentMediumIsNotSolid()
    {
        RegisterSurface("Ground", height: 4, Fixed64.Zero, (Fixed64)8);
        HeightmapTestNavigator navigator = CreateNavigator(
            new Vector3d(0, 1, 0),
            TraversalMedium.Gas,
            surfaceLevel: Fixed64.Zero);

        navigator.ConfigureHeightmapGrounding(HeightmapGroundingMode.SurfaceLevelAndPosition);

        navigator.ApplyHeightmapGrounding().Should().BeFalse();
        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Gas);
        navigator.FrameCondition.SurfaceLevel.Should().Be(Fixed64.Zero);
        navigator.Position.Y.Should().Be(Fixed64.One);
    }

    [Fact]
    public void TryApplyHeightmapGrounding_ShouldSkipRootProjection_WhenCorrectionExceedsSnapTolerance()
    {
        RegisterSurface("Ground", height: 5, Fixed64.Zero, (Fixed64)8);
        HeightmapTestNavigator navigator = CreateNavigator(new Vector3d(0, 1, 0), surfaceLevel: Fixed64.Zero);

        navigator.ConfigureHeightmapGrounding(
            HeightmapGroundingMode.SurfaceLevelAndPosition,
            snapTolerance: Fixed64.One);

        navigator.ApplyHeightmapGrounding().Should().BeTrue();
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)5);
        navigator.Position.Y.Should().Be(Fixed64.One);
        navigator.LastPosition.Y.Should().Be(Fixed64.One);
    }

    [Fact]
    public void TryApplyHeightmapGrounding_ConfiguredLayer_ShouldPreserveActiveLayerWhileValid()
    {
        RegisterSurface("Ground", height: 0, Fixed64.Zero, (Fixed64)5);
        RegisterSurface("Platform", height: 3, Fixed64.Zero, (Fixed64)5);
        HeightmapTestNavigator navigator = CreateNavigator(new Vector3d(
            Fixed64.Zero,
            (Fixed64)3 + PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY,
            Fixed64.Zero));

        navigator.ConfigureHeightmapGrounding(
            HeightmapGroundingMode.SurfaceLevelOnly,
            layerName: "Ground");

        navigator.ApplyHeightmapGrounding().Should().BeTrue();
        navigator.FrameCondition.SurfaceLevel.Should().Be(Fixed64.Zero);
        navigator.HeightmapGrounding.ActiveLayerName.Should().Be("Ground");

        navigator.ApplyHeightmapGrounding().Should().BeTrue();
        navigator.FrameCondition.SurfaceLevel.Should().Be(Fixed64.Zero);
        navigator.HeightmapGrounding.ActiveLayerName.Should().Be("Ground");
    }

    [Fact]
    public void TryApplyHeightmapGrounding_ShouldSupportJumpFromPlatformToDifferentHeightPlatform()
    {
        RegisterSurface("LowerPlatform", height: 1, Fixed64.Zero, (Fixed64)2, minBounds: Vector3d.Zero);
        RegisterSurface("UpperPlatform", height: 4, (Fixed64)3, (Fixed64)5, minBounds: new Vector3d(2, 0, 0));
        HeightmapTestNavigator navigator = CreateNavigator(new Vector3d(
            Fixed64.Zero,
            (Fixed64)1 + PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY,
            Fixed64.Zero));

        navigator.ConfigureHeightmapGrounding(HeightmapGroundingMode.SurfaceLevelAndPosition);
        navigator.ApplyHeightmapGrounding().Should().BeTrue();
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)1);
        navigator.HeightmapGrounding.ActiveLayerName.Should().Be("LowerPlatform");

        navigator.SetAirborne(surfaceLevel: (Fixed64)1);
        navigator.SetRootPosition(new Vector3d(
            (Fixed64)2,
            (Fixed64)4 + PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY,
            Fixed64.Zero));

        navigator.ApplyHeightmapGrounding().Should().BeFalse();
        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Gas);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)1);

        navigator.SetGroundContact(surfaceLevel: (Fixed64)4);

        navigator.ApplyHeightmapGrounding().Should().BeTrue();
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)4);
        navigator.Position.Y.Should().Be(
            (Fixed64)4 + PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY);
        navigator.LastPosition.Y.Should().Be(
            (Fixed64)4 + PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY);
        navigator.HeightmapGrounding.ActiveLayerName.Should().Be("UpperPlatform");
    }

    private static HeightmapTestNavigator CreateNavigator(
        Vector3d position,
        TraversalMedium medium = TraversalMedium.Solid,
        Fixed64? surfaceLevel = null,
        Fixed64? rootToFootOffsetY = null,
        Guid? globalId = null)
    {
        NavigationAgentProfile defaultProfile = PathTestFactory.DefaultNavigationProfile;
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                defaultProfile.Shape.Radius,
                defaultProfile.Shape.Height,
                rootToFootOffsetY ?? defaultProfile.Shape.RootToFootOffsetY),
            defaultProfile.MaxStepUp,
            defaultProfile.MaxDropDown,
            defaultProfile.ArrivalRadius,
            defaultProfile.AllowedMedia,
            defaultProfile.Capabilities);
        var navigator = new HeightmapTestNavigator(TestWorld.Context);
        navigator.Activate(
            new TrekCondition
            {
                Medium = medium,
                SurfaceLevel = surfaceLevel ?? Fixed64.Zero
            },
            position,
            profile,
            globalId: globalId);

        return navigator;
    }

    private static void RegisterSurface(
        string name,
        int height,
        Fixed64 minSelectionY,
        Fixed64 maxSelectionY,
        Vector3d? minBounds = null)
    {
        HeightmapSurface surface = HeightmapSurface.FromHeights(
            name,
            new Fixed64[1, 1] { { (Fixed64)height } },
            minBounds ?? Vector3d.Zero,
            Fixed64.One,
            new HeightmapCompression(Fixed64.Zero, Fixed64.One));

        TestWorld.Context.Heightmaps.Register(surface, minSelectionY, maxSelectionY).Should().BeTrue();
    }

    private sealed class HeightmapTestNavigator : Navigator
    {
        public HeightmapTestNavigator(TrailblazerWorldContext context)
            : base(context)
        {
        }

        public TrekCondition FrameCondition => _frameCondition;

        public bool ApplyHeightmapGrounding(
            bool updateMotorState = false,
            Fixed64? surfaceFriction = null,
            MotionTransfer motionTransfer = MotionTransfer.None)
        {
            return TryApplyHeightmapGrounding(updateMotorState, surfaceFriction, motionTransfer);
        }

        public bool ApplyHeightmapGroundingAfterConfig(HeightmapGroundingMode mode)
        {
            ConfigureHeightmapGrounding(mode);
            return ApplyHeightmapGrounding();
        }

        public void SetRootPosition(Vector3d position)
        {
            _position = position;
            _lastPosition = position;
        }

        public override void CheckTrekCondition()
        {
        }
    }
}
