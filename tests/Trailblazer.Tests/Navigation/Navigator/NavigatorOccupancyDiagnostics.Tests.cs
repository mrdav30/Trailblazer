using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using Chronicler.Diagnostics;
using Trailblazer.Heightmaps;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("TrailblazerLoggerCollection")]
public sealed class NavigatorOccupancyDiagnosticsTests : IDisposable
{
    private static readonly Guid SharedOccupancyId =
        new("30000000-0000-0000-0000-000000000001");
    private readonly DiagnosticLevel _originalMinimumLevel = TrailblazerLogger.MinimumLevel;
    private readonly Action<DiagnosticLevel, string, string> _originalLogHandler = TrailblazerLogger.LogHandler;

    public NavigatorOccupancyDiagnosticsTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
    }

    public void Dispose()
    {
        TrailblazerLogger.MinimumLevel = _originalMinimumLevel;
        TrailblazerLogger.LogHandler = _originalLogHandler;
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void FailedInitialAndProjectedOccupancyRegistrations_ShouldEmitExactDiagnostics()
    {
        var messages = new List<string>();
        TrailblazerLogger.MinimumLevel = DiagnosticLevel.Info;
        TrailblazerLogger.LogHandler = (_, message, _) => messages.Add(message);
        Guid sharedId = SharedOccupancyId;
        Fixed64 footOffset = PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY;
        TestNavigator incumbent = CreateNavigator(
            new Vector3d(Fixed64.Zero, footOffset, Fixed64.Zero), sharedId);

        _ = CreateNavigator(new Vector3d(Fixed64.Zero, footOffset, Fixed64.Zero), sharedId);

        var projected = CreateNavigator(
            new Vector3d(Fixed64.Zero, (Fixed64)3 + footOffset, Fixed64.Zero), sharedId);
        HeightmapSurface surface = HeightmapSurface.FromHeights(
            "Ground",
            new Fixed64[1, 1] { { Fixed64.Zero } },
            Vector3d.Zero,
            Fixed64.One,
            new HeightmapCompression(Fixed64.Zero, Fixed64.One));
        TestWorld.Context.Heightmaps.Register(surface, (Fixed64)(-4), (Fixed64)4)
            .Should().BeTrue();
        projected.ConfigureHeightmapGrounding(HeightmapGroundingMode.SurfaceLevelAndPosition);
        projected.ApplyHeightmapGrounding().Should().BeTrue();

        messages.Should().HaveCount(3);
        messages.Should().OnlyContain(message =>
            message.Contains($"Navigator {sharedId} failed to register occupancy", StringComparison.Ordinal));
        messages.Should().ContainSingle(message => message.Contains("position (0, 3.25, 0)", StringComparison.Ordinal));
        messages.Should().ContainSingle(message => message.Contains("voxel (4, 7, 4)", StringComparison.Ordinal));
        GridOccupantManager.GetOccupiedIndices(TestWorld.World, incumbent).Should().ContainSingle();
        GridOccupantManager.GetOccupiedIndices(TestWorld.World, projected).Should().BeEmpty();
    }

    [Fact]
    public void MovingWithinRegisteredVoxel_ShouldPreserveTicketWithoutRegistrationFailure()
    {
        Vector3d start = new(Fixed64.Zero,
            PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY, Fixed64.Zero);
        TestNavigator navigator = CreateNavigator(start, SharedOccupancyId);
        (_, Voxel voxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, start);
        GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, navigator, voxel.WorldIndex,
            out OccupantTicket originalTicket).Should().BeTrue();
        var messages = new List<string>();
        TrailblazerLogger.MinimumLevel = DiagnosticLevel.Info;
        TrailblazerLogger.LogHandler = (_, message, _) => messages.Add(message);

        try
        {
            navigator.AddPositionDelta(Vector3d.Right * Fixed64.FromFraction(1, 8));
            navigator.CommitFrameMotion();

            navigator.Position.Should().Be(start + Vector3d.Right * Fixed64.FromFraction(1, 8));
            GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, navigator, voxel.WorldIndex,
                out OccupantTicket currentTicket).Should().BeTrue();
            currentTicket.Should().Be(originalTicket);
            voxel.OccupantCount.Should().Be(1);
            GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator).Should().Equal(voxel.WorldIndex);
            messages.Should().BeEmpty("a valid within-cell move is not a failed registration");
        }
        finally { navigator.Reset(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingToAlreadyRegisteredTarget_ShouldReleasePreviousVoxel(bool fillTarget)
    {
        Vector3d start = new(Fixed64.Zero,
            PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY, Fixed64.Zero);
        TestNavigator navigator = CreateNavigator(start, SharedOccupancyId);
        (_, Voxel previous) = TestRequire.GridAndVoxelAt(TestWorld.Context, start);
        (VoxelGrid grid, Voxel target) = TestRequire.GridAndVoxelAt(TestWorld.Context, start + Vector3d.Right);
        grid.TryAddVoxelOccupant(target, navigator).Should().BeTrue();
        GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, navigator, target.WorldIndex,
            out OccupantTicket targetTicket).Should().BeTrue();
        var others = new List<TestNavigator>();
        try
        {
            if (fillTarget)
            {
                for (int i = 1; i < GridOccupantManager.MaxOccupantCount; i++)
                    others.Add(CreateNavigator(start + Vector3d.Right, new Guid(i, 0, 0, new byte[8])));
                target.HasVacancy.Should().BeFalse();
            }

            navigator.AddPositionDelta(Vector3d.Right);
            navigator.CommitFrameMotion();

            previous.OccupantCount.Should().Be(0);
            target.OccupantCount.Should().Be(fillTarget ? byte.MaxValue : (byte)1);
            GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator).Should().Equal(target.WorldIndex);
            GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, navigator, target.WorldIndex,
                out OccupantTicket retainedTicket).Should().BeTrue();
            retainedTicket.Should().Be(targetTicket);
        }
        finally
        {
            navigator.Reset();
            foreach (TestNavigator other in others) other.Reset();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingWithinVoxelAfterExternalRemoval_ShouldRestoreRegistration(bool reuseSlot)
    {
        Vector3d start = new(Fixed64.Zero,
            PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY, Fixed64.Zero);
        TestNavigator navigator = CreateNavigator(start, SharedOccupancyId);
        (VoxelGrid grid, Voxel voxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, start);
        TestNavigator? other = null;
        try
        {
            for (int i = 0; i < 2; i++)
            {
                navigator.AddPositionDelta(Vector3d.Right * Fixed64.FromFraction(1, 8));
                navigator.CommitFrameMotion();
            }
            GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, navigator, voxel.WorldIndex,
                out OccupantTicket originalTicket).Should().BeTrue();
            grid.TryRemoveVoxelOccupant(voxel, navigator).Should().BeTrue();
            voxel.OccupantCount.Should().Be(0);
            OccupantTicket otherTicket = default;
            if (reuseSlot)
            {
                other = CreateNavigator(start, new Guid("30000000-0000-0000-0000-000000000002"));
                GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, other, voxel.WorldIndex,
                    out otherTicket).Should().BeTrue();
                otherTicket.Slot.Should().Be(originalTicket.Slot);
                otherTicket.Generation.Should().NotBe(originalTicket.Generation);
            }

            navigator.AddPositionDelta(Vector3d.Right * Fixed64.FromFraction(1, 8));
            navigator.CommitFrameMotion();

            voxel.OccupantCount.Should().Be(reuseSlot ? (byte)2 : (byte)1);
            GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator).Should().Equal(voxel.WorldIndex);
            GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, navigator, voxel.WorldIndex,
                out OccupantTicket repairedTicket).Should().BeTrue();
            repairedTicket.Should().NotBe(originalTicket);
            grid.TryGetVoxelOccupant(voxel, repairedTicket, out var repairedOwner).Should().BeTrue();
            repairedOwner.Should().BeSameAs(navigator);
            if (other != null)
            {
                grid.TryGetVoxelOccupant(voxel, otherTicket, out var retainedOwner).Should().BeTrue();
                retainedOwner.Should().BeSameAs(other);
                GridOccupantManager.GetOccupiedIndices(TestWorld.World, other).Should().Equal(voxel.WorldIndex);
            }
        }
        finally
        {
            navigator.Reset();
            other?.Reset();
        }
    }

    [Fact]
    public void MovingWithinVoxelAfterExternalReregistration_ShouldPreserveNewTicket()
    {
        Vector3d start = new(Fixed64.Zero,
            PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY, Fixed64.Zero);
        TestNavigator navigator = CreateNavigator(start, SharedOccupancyId);
        (VoxelGrid grid, Voxel voxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, start);
        try
        {
            for (int i = 0; i < 2; i++)
            {
                navigator.AddPositionDelta(Vector3d.Right * Fixed64.FromFraction(1, 8));
                navigator.CommitFrameMotion();
            }
            GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, navigator, voxel.WorldIndex,
                out OccupantTicket originalTicket).Should().BeTrue();
            grid.TryRemoveVoxelOccupant(voxel, navigator).Should().BeTrue();
            grid.TryAddVoxelOccupant(voxel, navigator).Should().BeTrue();
            GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, navigator, voxel.WorldIndex,
                out OccupantTicket replacementTicket).Should().BeTrue();
            replacementTicket.Generation.Should().NotBe(originalTicket.Generation);

            navigator.AddPositionDelta(Vector3d.Right * Fixed64.FromFraction(1, 8));
            navigator.CommitFrameMotion();

            GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, navigator, voxel.WorldIndex,
                out OccupantTicket retainedTicket).Should().BeTrue();
            retainedTicket.Should().Be(replacementTicket);
            grid.TryGetVoxelOccupant(voxel, retainedTicket, out var owner).Should().BeTrue();
            owner.Should().BeSameAs(navigator);
            voxel.OccupantCount.Should().Be(1);
            GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator).Should().Equal(voxel.WorldIndex);
        }
        finally { navigator.Reset(); }
    }

    [Fact]
    public void MovingAfterCompletedGridReplacement_ShouldRegisterInNewGridLifetime()
    {
        Vector3d start = new(Fixed64.Zero,
            PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY, Fixed64.Zero);
        TestNavigator navigator = CreateNavigator(start, SharedOccupancyId);
        (VoxelGrid grid, Voxel voxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, start);
        try
        {
            for (int i = 0; i < 2; i++)
            {
                navigator.AddPositionDelta(Vector3d.Right * Fixed64.FromFraction(1, 8));
                navigator.CommitFrameMotion();
            }
            WorldVoxelIndex previousIndex = voxel.WorldIndex;
            GridConfiguration configuration = grid.Configuration;
            TestWorld.World.TryRemoveGrid(grid.GridIndex).Should().BeTrue();
            GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator).Should().BeEmpty();
            TestWorld.World.TryAddGrid(configuration, out _).Should().BeTrue();
            (VoxelGrid replacementGrid, Voxel replacementVoxel) =
                TestRequire.GridAndVoxelAt(TestWorld.Context, navigator.Position);
            replacementVoxel.WorldIndex.GridSpawnToken.Should().NotBe(previousIndex.GridSpawnToken);

            navigator.AddPositionDelta(Vector3d.Right * Fixed64.FromFraction(1, 8));
            navigator.CommitFrameMotion();

            GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator)
                .Should().Equal(replacementVoxel.WorldIndex);
            GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, navigator, replacementVoxel.WorldIndex,
                out OccupantTicket ticket).Should().BeTrue();
            replacementGrid.TryGetVoxelOccupant(replacementVoxel, ticket, out var owner).Should().BeTrue();
            owner.Should().BeSameAs(navigator);
            replacementVoxel.OccupantCount.Should().Be(1);
        }
        finally { navigator.Reset(); }
    }

    [Fact]
    public void MovingAfterRegistrationIsRemovedByAddCallback_ShouldRestoreRegistrationOnNextMove()
    {
        Vector3d start = new(Fixed64.Zero,
            PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY, Fixed64.Zero);
        TestNavigator navigator = CreateNavigator(start, SharedOccupancyId);
        (VoxelGrid grid, Voxel voxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, start);
        int additions = 0;
        Action<OccupantEventInfo> removeFirstAddition = _ =>
        {
            if (++additions == 1)
                grid.TryRemoveVoxelOccupant(voxel, navigator).Should().BeTrue();
        };
        try
        {
            grid.TryRemoveVoxelOccupant(voxel, navigator).Should().BeTrue();
            voxel.OnOccupantAdded += removeFirstAddition;

            navigator.AddPositionDelta(Vector3d.Right * Fixed64.FromFraction(1, 8));
            navigator.CommitFrameMotion();

            additions.Should().Be(1);
            voxel.OccupantCount.Should().Be(0);
            GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator).Should().BeEmpty();

            navigator.AddPositionDelta(Vector3d.Right * Fixed64.FromFraction(1, 8));
            navigator.CommitFrameMotion();

            additions.Should().Be(2);
            voxel.OccupantCount.Should().Be(1);
            GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator).Should().Equal(voxel.WorldIndex);
            GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, navigator, voxel.WorldIndex,
                out OccupantTicket ticket).Should().BeTrue();
            grid.TryGetVoxelOccupant(voxel, ticket, out var owner).Should().BeTrue();
            owner.Should().BeSameAs(navigator);
        }
        finally
        {
            voxel.OnOccupantAdded -= removeFirstAddition;
            navigator.Reset();
        }
    }

    [Fact]
    public void SameCellResolution_AfterAddCallbackCorruptsVoxelIndex_ShouldRetainGridDiagnostic()
    {
        Vector3d start = new(Fixed64.Zero,
            PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY, Fixed64.Zero);
        TestNavigator navigator = CreateNavigator(start, SharedOccupancyId);
        (VoxelGrid grid, Voxel voxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, start);
        WorldVoxelIndex originalAddress = voxel.WorldIndex;
        DiagnosticLevel originalLevel = GridForgeLogger.MinimumLevel;
        Action<DiagnosticLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        var messages = new List<(DiagnosticLevel Level, string Message)>();
        int additions = 0;
        Action<OccupantEventInfo> corruptIdentity = ignoredEvent =>
        {
            additions++;
            WorldVoxelIndex malformed = originalAddress;
            malformed.VoxelIndex = new VoxelIndex(-1, originalAddress.VoxelIndex.y, originalAddress.VoxelIndex.z);
            voxel.WorldIndex = malformed;
        };
        try
        {
            grid.TryRemoveVoxelOccupant(voxel, navigator).Should().BeTrue();
            GridForgeLogger.MinimumLevel = DiagnosticLevel.Info;
            GridForgeLogger.LogHandler = (level, message, _) => messages.Add((level, message));
            voxel.OnOccupantAdded += corruptIdentity;

            NavigatorOccupancyTracker.Update(TestWorld.World, navigator,
                start + Vector3d.Right * Fixed64.FromFraction(1, 8), start, init: false);

            additions.Should().Be(1);
            voxel.Index.x.Should().Be(-1);
            messages.Should().ContainSingle();
            messages[0].Level.Should().Be(DiagnosticLevel.Info);
            messages[0].Message.Should().Contain($"(-1, {originalAddress.VoxelIndex.y}, {originalAddress.VoxelIndex.z})");
            GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator).Should().Equal(originalAddress);
        }
        finally
        {
            voxel.OnOccupantAdded -= corruptIdentity;
            voxel.WorldIndex = originalAddress;
            GridForgeLogger.MinimumLevel = originalLevel;
            GridForgeLogger.LogHandler = originalHandler;
            navigator.Reset();
        }
    }

    [Fact]
    public void ResetAfterRemovalCallbackRefreshesOccupancy_ShouldRegisterReactivatedIdentity()
    {
        Vector3d start = new(Fixed64.Zero,
            PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY, Fixed64.Zero);
        var navigator = new OccupancyRefreshNavigator();
        var condition = new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        };
        navigator.Activate(TestWorld.Context, condition, start,
            PathTestFactory.DefaultNavigationProfile, globalId: SharedOccupancyId);
        (VoxelGrid grid, Voxel voxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, start);
        int callbacks = 0;
        bool callbackCompleted = false;
        Action<OccupantEventInfo> restoreRegistration = _ =>
        {
            callbacks++;
            grid.TryAddVoxelOccupant(voxel, navigator).Should().BeTrue();
            navigator.RefreshOccupancy();
            callbackCompleted = true;
        };
        try
        {
            voxel.OnOccupantRemoved += restoreRegistration;

            navigator.Reset();

            voxel.OnOccupantRemoved -= restoreRegistration;
            callbacks.Should().Be(1);
            callbackCompleted.Should().BeTrue();
            Guid replacementId = new("30000000-0000-0000-0000-000000000002");
            navigator.Activate(TestWorld.Context, condition, start,
                PathTestFactory.DefaultNavigationProfile, globalId: replacementId);

            navigator.GlobalId.Should().Be(replacementId);
            GridOccupantManager.TryGetOccupancyTicket(TestWorld.World, navigator, voxel.WorldIndex,
                out OccupantTicket currentIdentityTicket).Should().BeTrue(
                    "a callback's registration under the old identity cannot replace registration of the reactivated identity");
            grid.TryGetVoxelOccupant(voxel, currentIdentityTicket, out var owner).Should().BeTrue();
            owner.Should().BeSameAs(navigator);
        }
        finally
        {
            voxel.OnOccupantRemoved -= restoreRegistration;
            navigator.Reset();
        }
    }

    [Fact]
    public void MovingToFullUnregisteredTarget_ShouldPreservePreviousRegistration()
    {
        Vector3d start = new(Fixed64.Zero,
            PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY, Fixed64.Zero);
        TestNavigator navigator = CreateNavigator(start, SharedOccupancyId);
        (_, Voxel previous) = TestRequire.GridAndVoxelAt(TestWorld.Context, start);
        (_, Voxel target) = TestRequire.GridAndVoxelAt(TestWorld.Context, start + Vector3d.Right);
        var others = new List<TestNavigator>();
        try
        {
            for (int i = 1; i <= GridOccupantManager.MaxOccupantCount; i++)
                others.Add(CreateNavigator(start + Vector3d.Right, new Guid(i, 0, 0, new byte[8])));
            target.HasVacancy.Should().BeFalse();

            navigator.AddPositionDelta(Vector3d.Right);
            navigator.CommitFrameMotion();

            previous.OccupantCount.Should().Be(1);
            target.OccupantCount.Should().Be(byte.MaxValue);
            GridOccupantManager.GetOccupiedIndices(TestWorld.World, navigator).Should().Equal(previous.WorldIndex);
        }
        finally
        {
            navigator.Reset();
            foreach (TestNavigator other in others) other.Reset();
        }
    }

    private sealed class OccupancyRefreshNavigator : Navigator
    {
        public void RefreshOccupancy() => CheckVoxelOccupancy(init: true);

        public override void CheckTrekCondition() { }
    }

    private static TestNavigator CreateNavigator(Vector3d position, Guid globalId)
    {
        var navigator = new TestNavigator(TestWorld.Context);
        navigator.Activate(
            new TrekCondition
            {
                Medium = TraversalMedium.Solid,
                SurfaceLevel = Fixed64.Zero,
                GroundState = new GroundCondition()
            },
            position,
            PathTestFactory.DefaultNavigationProfile,
            globalId: globalId);
        return navigator;
    }
}
