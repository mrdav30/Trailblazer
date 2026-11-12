using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DSupportTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ReplacementChecksAuthoredCoverageWhenTheCenterTouchesAClosedSeam(int rawOffset)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        context.World.TryAddGrid(GuidedGrid, out _).Should().BeTrue();
        var limited = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Quarter, Fixed64.Two);
        var map = new NavigationMapBuilder("side", GuidedGrid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One)
            .AddCell(GuidedAddress(1).Index, limited).Build();
        PublishGuidedMap(context, map, 1);

        using var host = new PostureHost(context, new Vector2d(Fixed64.Half + Fixed64.FromRaw(rawOffset), Fixed64.Zero),
            Circle(Fixed64.FromFraction(1, 8)), default(NavigationMotorSettings2D));
        host.TryReplaceNavigationProfile(Circle(Fixed64.Half)).Should()
            .Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);
        host.HostCalls.Should().Be(0);

        static NavigationAgentProfile2D Circle(Fixed64 radius) => new(NavigationMode2D.TopDown, radius, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementIgnoresSupportOnAShiftedHexLattice(bool refreshSupport)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop);
        var original = new GridConfiguration(Vector3d.Zero, new Vector3d(0, 1, 0),
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics);
        var shifted = new GridConfiguration(new Vector3d(Fixed64.Quarter, Fixed64.Zero, Fixed64.Zero),
            new Vector3d(Fixed64.Quarter, Fixed64.One, Fixed64.Zero),
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics);
        var support = new VoxelIndex(0, 1, 0);
        Publish(original, "side", Fixed64.Zero, Fixed64.Two, 1);
        Publish(shifted, "shifted", Fixed64.Quarter, Fixed64.Half, 2);

        using var host = new PostureHost(context, new Vector2d(Fixed64.Zero, Fixed64.Quarter), Posture(Fixed64.Zero));
        host.SupportSample = new NavigationSupport2D(Vector2d.Zero, Vector2d.Forward, Fixed64.One);
        if (refreshSupport)
            host.RefreshSupport();
        host.TryReplaceNavigationProfile(Posture(Fixed64.Quarter)).Should().Be(NavigationProfileReplacementStatus.Applied);
        host.Pose.Center.Should().Be(new Vector2d(Fixed64.Zero, Fixed64.FromFraction(3, 8)));
        host.HostCalls.Should().Be(1);

        void Publish(GridConfiguration grid, string id, Fixed64 x, Fixed64 height, long sequence)
        {
            context.World.TryAddGrid(grid, out _).Should().BeTrue();
            var cell = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
                Fixed64.Zero, Fixed64.One, height);
            var map = new NavigationMapBuilder(id, grid).SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One)
                .SetDefaultCell(cell)
                .AddPlanarSupport(support, new(x - Fixed64.Half, Fixed64.Zero), new(x + Fixed64.Half, Fixed64.Zero))
                .Build();
            PublishGuidedMap(context, map, sequence);
        }
    }

    [Theory]
    [InlineData(false, 7, NavigationProfileReplacementStatus.Applied)]
    [InlineData(false, 6, NavigationProfileReplacementStatus.NavigationClearanceRejected)]
    [InlineData(true, 128, NavigationProfileReplacementStatus.NavigationClearanceRejected)]
    public void ReplacementChecksTangentSupportOnAnAlignedCrossMapNeighbor(bool limitHeight, int lookupProbes,
        NavigationProfileReplacementStatus expected)
    {
        var sample = new GuideSampleWorkBudget(lookupProbes, 128, 8, 32, 32, 32, 1);
        using var context = TrailblazerWorldContext.CreateOwned(settings: PlanarSchedulingTestSettings.Create(guideSampleBudget: sample));
        var above = new GridConfiguration(Vector3d.Zero, new Vector3d(0, 1, 0));
        var below = new GridConfiguration(new Vector3d(0, 0, -1), new Vector3d(0, 1, -1));
        var support = new VoxelIndex(0, 1, 0);
        var floor = -Vector2d.Forward * Fixed64.Half;
        var open = new NavigationCell(TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        var solid = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, limitHeight ? Fixed64.Half : Fixed64.One);
        context.World.TryAddGrid(above, out _).Should().BeTrue();
        context.World.TryAddGrid(below, out _).Should().BeTrue();
        PublishGuidedMap(context, new NavigationMapBuilder("side", above)
            .SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One).SetDefaultCell(open).Build(), 1);
        PublishGuidedMap(context, new NavigationMapBuilder("floor", below)
            .SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One).AddCell(support, solid)
            .AddPlanarSupport(support, floor - Vector2d.Right * Fixed64.Half, floor + Vector2d.Right * Fixed64.Half)
            .Build(), 2);

        using var host = new PostureHost(context, floor + Vector2d.Forward * Fixed64.Quarter, Posture(Fixed64.Zero));
        host.SupportSample = new NavigationSupport2D(floor, Vector2d.Forward, Fixed64.One);
        host.RefreshSupport();
        host.TryReplaceNavigationProfile(Posture(Fixed64.Quarter)).Should().Be(expected);
        host.HostCalls.Should().Be(expected == NavigationProfileReplacementStatus.Applied ? 1 : 0);
    }

    [Fact]
    public void FreeMotorReplacementCannotLeaveAnAuthoredCenterForTheHostOnlyFallback()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(0, 1, 0));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var cell = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        PublishGuidedMap(context, new NavigationMapBuilder("side", grid)
            .SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One).SetDefaultCell(cell).Build(), 1);
        var center = Vector2d.Forward * Fixed64.FromFraction(3, 8);
        using var host = new PostureHost(context, center, Posture(Fixed64.Zero));
        host.TryReplaceNavigationProfile(Posture(Fixed64.Half)).Should()
            .Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);
        host.Pose.Center.Should().Be(center);
        host.HostCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementRejectsMissingOrSuppressedAuthoredCenter(bool suppressed)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var grid = new GridConfiguration(GuidedGrid.BoundsMin, GuidedGrid.BoundsMax, storageKind: GridStorageKind.Sparse);
        var center = GuidedAddress(0).Index;
        var neighbor = GuidedAddress(1).Index;
        context.World.TryAddGrid(grid, suppressed ? new[] { center, neighbor } : new[] { neighbor }, out _)
            .Should().BeTrue();
        var solid = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        PublishGuidedMap(context, new NavigationMapBuilder("side", grid)
            .SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One).AddCell(center, solid).Build(), 1);
        if (suppressed)
        {
            var delta = new NavigationMapOverlayDelta("side", new[] { NavigationCellOverlayOperation.Suppress(center) });
            var operation = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(new NavigationOverlayTransaction(
                new[] { delta })), 2, context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            for (int i = 0; i < 8; i++) { context.Simulate(); context.LateSimulate(); }
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        }
        using var host = new PostureHost(context);
        context.Simulate();
        host.PrepareFrameMotion();
        host.CommitFrameMotion();
        context.LateSimulate();
        host.LastCommittedCell.Should().BeNull();
        host.TryReplaceNavigationProfile(Posture(Fixed64.Zero)).Should()
            .Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);
        host.HostCalls.Should().Be(0);
    }

    [Fact]
    public void PostureCyclesPreserveTheFootAndReacquireTheSameDestination()
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new PostureHost(context);
        host.RefreshSupport();
        host.ApplySideViewGuidedTrekRequest(GuidedAddress(0), GuidedAddress(1), SideViewPolicy,
            PathAlgorithm.AStar, SideViewBudget).Should().Be(NavigationGuideStatus.Success);

        var profiles = new[] { Posture(Fixed64.Quarter), Posture(Fixed64.Zero), Profile };
        foreach (var replacement in profiles)
        {
            host.TryReplaceNavigationProfile(replacement).Should().Be(NavigationProfileReplacementStatus.Applied);
            host.NavigationProfile.Should().Be(replacement);
            host.HostProfile.Should().Be(replacement);
            host.Pose.Center.Should().Be(host.HostCenter);
            (host.Pose.Center.Y - replacement.NominalFootOffset).Should().Be(-Fixed64.Half);
            host.Support.Should().BeNull();
            host.IsGrounded.Should().BeFalse();
            host.GuidanceStatus.Should().Be(NavigationGuideStatus.Pending);
            host.Velocity.Should().Be(Vector2d.Zero);
            host.RefreshSupport();
        }

        for (int frame = 0; frame < 16 && !host.HasArrived; frame++)
        {
            context.Simulate();
            host.PrepareFrameMotion();
            host.CommitFrameMotion();
            context.LateSimulate();
        }
        host.HasArrived.Should().BeTrue();
        host.Pose.Center.Should().Be(Vector2d.Right);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void ProfileReplacementSeparatesPublishedClearanceAndHostRejection()
    {
        var roof = new VoxelIndex(0, 1, 2);
        var open = new NavigationCell(TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        var solid = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        var map = new NavigationMapBuilder("side", GuidedGrid).SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One)
            .SetDefaultCell(solid)
            .AddPlanarSupport(GuidedAddress(0).Index, new(-Fixed64.Half, -Fixed64.Half), new(Fixed64.Half, -Fixed64.Half))
            .AddCell(roof, open)
            .AddPlanarSupport(roof, new(-Fixed64.Half, Fixed64.FromFraction(5, 8)), new(Fixed64.Half, Fixed64.FromFraction(5, 8)))
            .Build();
        using var context = GuidedScene(map);
        using var host = new PostureHost(context);
        host.RefreshSupport();
        host.TryReplaceNavigationProfile(Posture(Fixed64.One)).Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);
        host.HostCalls.Should().Be(0);
        host.AcceptReplacement = false;
        host.TryReplaceNavigationProfile(Posture(Fixed64.Zero)).Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);
        host.HostCalls.Should().Be(1);
        host.NavigationProfile.Should().Be(Profile);
        host.HostProfile.Should().Be(Profile);
        host.Pose.Center.Should().Be(Vector2d.Zero);
        host.IsGrounded.Should().BeTrue();
        host.AcceptReplacement = true;
        host.TryReplaceNavigationProfile(Posture(Fixed64.FromFraction(5, 8)))
            .Should().Be(NavigationProfileReplacementStatus.Applied);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReplacementReportsNotificationFailureAfterPublishingBothEnvelopes(bool cellFailure, bool hostFailure)
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new PostureHost(context);
        var compact = Posture(Fixed64.Zero);
        var physical = new InvalidOperationException("Physical notification failed");
        var committedCell = new InvalidOperationException("Committed-cell notification failed");
        host.HostFailure = hostFailure ? physical : null;
        if (cellFailure)
            host.CommittedCellChanged += _ => throw committedCell;
        Action change = () => host.TryReplaceNavigationProfile(compact);
        if (cellFailure && hostFailure)
            change.Should().Throw<AggregateException>().Which.InnerExceptions.Should().Equal(committedCell, physical);
        else
            change.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(cellFailure ? committedCell : physical);
        host.HostProfile.Should().Be(compact);
        host.NavigationProfile.Should().Be(compact);
        host.Pose.Center.Should().Be(host.HostCenter);
        host.TryReplaceNavigationProfile(compact).Should().Be(NavigationProfileReplacementStatus.Unchanged);
    }

    [Fact]
    public void ReplacementHonorsFullVoxelCapacityAndAdoptsItsExistingTicket()
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new PostureHost(context);
        var position = new Vector3d(Fixed64.Zero, Fixed64.One, Fixed64.Zero);
        context.World.TryGetGrid(position, out var containingGrid).Should().BeTrue();
        var grid = TestRequire.NotNull(containingGrid);
        context.World.TryGetVoxel(position, out var found).Should().BeTrue();
        var voxel = TestRequire.NotNull(found);
        var last = new OccupancyFixture(new Guid(1, 0, 0, new byte[8]), position);
        grid.TryAddVoxelOccupant(voxel, last).Should().BeTrue();
        for (int i = 1; i < GridOccupantManager.MaxOccupantCount; i++)
            grid.TryAddVoxelOccupant(voxel, new OccupancyFixture(new Guid(i + 1, 0, 0, new byte[8]), position)).Should().BeTrue();

        var compact = Posture(Fixed64.Zero);
        host.TryReplaceNavigationProfile(compact).Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);
        host.HostCalls.Should().Be(0);
        grid.TryRemoveVoxelOccupant(voxel, last).Should().BeTrue();
        host.RefreshSupport();
        context.Simulate();
        host.PrepareFrameMotion();
        host.CommitFrameMotion();
        context.LateSimulate();
        voxel.HasVacancy.Should().BeFalse();
        host.TryReplaceNavigationProfile(compact).Should().Be(NavigationProfileReplacementStatus.Applied);
        voxel.HasVacancy.Should().BeFalse();
    }

    [Fact]
    public void ReplacementDoesNotDiscardAnUncoveredTrekOrUseAnUnavailablePolicy()
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new PostureHost(context);
        host.RefreshSupport();
        host.ApplySideViewGuidedTrekRequest(GuidedAddress(0), GuidedAddress(1), SideViewPolicy,
            PathAlgorithm.AStar, SideViewBudget).Should().Be(NavigationGuideStatus.Success);
        host.TryReplaceNavigationProfile(Posture((Fixed64)4)).Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.Success);
        host.ApplySideViewGuidedTrekRequest(GuidedAddress(0), GuidedAddress(1), new NavigationAreaPolicyKey("missing", 1),
            PathAlgorithm.AStar, SideViewBudget).Should().Be(NavigationGuideStatus.Stale);
        host.TryReplaceNavigationProfile(Posture(Fixed64.Zero)).Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);
        host.HostCalls.Should().Be(0);
    }

    [Fact]
    public void UnmappedFreeMotorUsesHostFitAndPublisherExpiresAfterCommit()
    {
        using var context = GuidedStepScene(flat: true);
        var center = new Vector2d((Fixed64)10, Fixed64.Zero);
        using var passive = new Host(context, pose: new NavigationPose2D(center, Fixed64.Zero));
        passive.TryReplaceNavigationProfile(Posture(Fixed64.Zero)).Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);

        using var host = new PostureHost(context, center);
        var compact = Posture(Fixed64.Zero);
        host.TryReplaceNavigationProfile(compact).Should().Be(NavigationProfileReplacementStatus.Applied);
        host.Pose.Center.Should().Be(new Vector2d(center.X, -Fixed64.Quarter));
        host.LastCommittedCell.Should().BeNull();
        host.LastPublisher!.Should().Throw<InvalidOperationException>();
        host.SkipPublisher = true;
        Action failedContract = () => host.TryReplaceNavigationProfile(Profile);
        failedContract.Should().Throw<InvalidOperationException>();
        host.NavigationProfile.Should().Be(compact);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ReplacementChecksPublishedSupportLimitsBeforeHostFit(bool radius, bool refreshSupport)
    {
        var limited = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Quarter, Fixed64.One);
        var open = new NavigationCell(TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        var source = GuidedAddress(0).Index;
        var map = new NavigationMapBuilder("side", GuidedGrid).SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One)
            .SetDefaultCell(open).AddCell(source, limited)
            .AddPlanarSupport(source, new(-Fixed64.Half, -Fixed64.Half), new(Fixed64.Half, -Fixed64.Half)).Build();
        using var context = GuidedScene(map);
        using var host = new PostureHost(context);
        if (refreshSupport)
            host.RefreshSupport();
        var replacement = radius
            ? new NavigationAgentProfile2D(Profile.Mode, Fixed64.FromFraction(3, 8), Profile.AxisLength, Profile.MaxStepUp,
                Profile.MaxDropDown, Profile.MinimumSupportNormalDot, Profile.ArrivalRadius, Profile.AllowedMedia, Profile.Capabilities)
            : Posture(Fixed64.One);
        host.TryReplaceNavigationProfile(replacement).Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);
        host.HostCalls.Should().Be(0);
        host.NavigationProfile.Should().Be(Profile);
        host.IsGrounded.Should().Be(refreshSupport);
    }

    [Fact]
    public void OddAxisReplacementUsesTheExactNominalFootConvention()
    {
        using var context = GuidedStepScene(flat: true);
        var aboveFloor = Vector2d.Forward * Fixed64.FromFraction(1, 16);
        using var host = new PostureHost(context, center: aboveFloor);
        var odd = Posture(Profile.AxisLength + Fixed64.MinIncrement);
        host.TryReplaceNavigationProfile(odd).Should().Be(NavigationProfileReplacementStatus.Applied);
        host.Pose.Center.Should().Be(aboveFloor);
    }

    [Theory]
    [InlineData("height", NavigationProfileReplacementStatus.NavigationClearanceRejected)]
    [InlineData("capability", NavigationProfileReplacementStatus.NavigationClearanceRejected)]
    [InlineData("missing", NavigationProfileReplacementStatus.NavigationClearanceRejected)]
    [InlineData("suppressed", NavigationProfileReplacementStatus.NavigationClearanceRejected)]
    [InlineData("clear", NavigationProfileReplacementStatus.Applied)]
    public void ReplacementChecksTangentSupportBelowTheCenterCell(string condition, NavigationProfileReplacementStatus expected)
    {
        var limited = new NavigationCell(TraversalMedia.Solid,
            condition == "capability" ? TraversalCapability.Jump : TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Quarter, condition == "height" ? Fixed64.Half : Fixed64.One);
        var open = new NavigationCell(TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        var source = GuidedAddress(0).Index;
        var floor = new Vector2d(Fixed64.Zero, Fixed64.Half);
        var builder = new NavigationMapBuilder("side", GuidedGrid).SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One)
            .AddCell(new VoxelIndex(0, 1, 2), open)
            .AddPlanarSupport(source, floor - Vector2d.Right * Fixed64.Half, floor + Vector2d.Right * Fixed64.Half);
        if (condition != "missing")
            builder.AddCell(source, limited);
        using var context = GuidedScene(builder.Build());
        if (condition == "suppressed")
        {
            var delta = new NavigationMapOverlayDelta("side", new[] { NavigationCellOverlayOperation.Suppress(source) });
            var operation = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(new NavigationOverlayTransaction(
                new[] { delta })), 3, context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            for (int i = 0; i < 8; i++) { context.Simulate(); context.LateSimulate(); }
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        }
        using var host = new PostureHost(context, center: floor + Vector2d.Forward * Fixed64.Quarter,
            initial: Posture(Fixed64.Zero));
        host.SupportSample = new NavigationSupport2D(floor, Vector2d.Forward, Fixed64.One);
        host.RefreshSupport();
        host.TryReplaceNavigationProfile(Posture(Fixed64.Quarter, TraversalCapability.None)).Should().Be(expected);
        host.HostCalls.Should().Be(expected == NavigationProfileReplacementStatus.Applied ? 1 : 0);
    }

    [Fact]
    public void ReplacementCanUseHostSupportInAPlanarCellWithNoAuthoredSurface()
    {
        using var context = Scene();
        var alias = new GridConfiguration(new Vector3d(20, 0, 20), new Vector3d(21, 1, 21),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.Two, Fixed64.One, Fixed64.One));
        context.World.TryAddGrid(alias, out _).Should().BeTrue();
        PublishGuidedMap(context, new NavigationMapBuilder("alias", alias)
            .SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One).Build(), 3);
        using var host = new PostureHost(context);
        host.RefreshSupport();
        host.TryReplaceNavigationProfile(Posture(Fixed64.Zero)).Should().Be(NavigationProfileReplacementStatus.Applied);
    }

    [Fact]
    public void FootPreservingShrinkCanClearTheOldSlopedContact()
    {
        using var context = GuidedScene(PathTestFactory.BuildSideViewValley(GuidedGrid).Build());
        var profile = new NavigationAgentProfile2D(Profile.Mode, Fixed64.FromFraction(15, 64), Profile.AxisLength,
            Profile.MaxStepUp, Profile.MaxDropDown, Profile.MinimumSupportNormalDot, Profile.ArrivalRadius,
            Profile.AllowedMedia, Profile.Capabilities);
        context.Pathing.ResolveSideViewAnchor(GuidedAddress(1), profile, SideViewPolicy, SideViewBudget, out var start)
            .Should().Be(NavigationGuideStatus.Success);
        using var host = new PostureHost(context, start, profile);
        host.SupportSample = new NavigationSupport2D(new Vector2d(Fixed64.One, Fixed64.FromFraction(1, 8)),
            new Vector2d(-Fixed64.FromFraction(3, 5), Fixed64.FromFraction(4, 5)), Fixed64.One);
        host.RefreshSupport();
        var smaller = new NavigationAgentProfile2D(Profile.Mode, profile.Radius - Fixed64.FromFraction(1, 32),
            Profile.AxisLength, Profile.MaxStepUp, Profile.MaxDropDown, Profile.MinimumSupportNormalDot,
            Profile.ArrivalRadius, Profile.AllowedMedia, Profile.Capabilities);
        host.TryReplaceNavigationProfile(smaller).Should().Be(NavigationProfileReplacementStatus.Applied);
        host.Pose.Center.X.Should().Be(start.X);
        host.IsGrounded.Should().BeFalse();
    }

    [Fact]
    public void ReplacementRequiresTheBoundPublicationEvenForTheFreeMotor()
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new PostureHost(context, new Vector2d((Fixed64)10, Fixed64.Zero));
        var removal = new NavigationMapRemoveOperation("side", 3, context.FrameCount + 1);
        context.Pathing.Admit(removal).Should().BeTrue();
        for (int frame = 0; frame < 8; frame++)
        {
            context.Simulate();
            context.LateSimulate();
        }
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        host.TryReplaceNavigationProfile(Posture(Fixed64.Zero))
            .Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);
        host.HostCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementChecksBothTangentSupportsAtAClosedSeam(bool limitLeft)
    {
        var open = new NavigationCell(TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        var floor = new Vector2d(Fixed64.Half, Fixed64.Half);
        var map = new NavigationMapBuilder("side", GuidedGrid).SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One)
            .SetDefaultCell(open);
        for (int i = 0; i < 2; i++)
        {
            var cell = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
                Fixed64.Zero, Fixed64.Quarter, ((i == 0) == limitLeft) ? Fixed64.Half : Fixed64.One);
            var index = GuidedAddress(i).Index;
            var left = floor + Vector2d.Right * (Fixed64)(i - 1);
            map.AddCell(index, cell).AddPlanarSupport(index, left, left + Vector2d.Right);
        }
        using var context = GuidedScene(map.Build());
        using var host = new PostureHost(context, floor + Vector2d.Forward * Fixed64.Quarter, Posture(Fixed64.Zero));
        host.SupportSample = new NavigationSupport2D(floor, Vector2d.Forward, Fixed64.One);
        host.RefreshSupport();
        host.TryReplaceNavigationProfile(Posture(Fixed64.Quarter))
            .Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);
        host.HostCalls.Should().Be(0);
    }

    [Fact]
    public void ReplacementRecoveryRequiresAFreshPublishedSupportWitness()
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new PostureHost(context);
        host.RefreshSupport();
        host.ApplySideViewGuidedTrekRequest(GuidedAddress(0), GuidedAddress(1), SideViewPolicy,
            PathAlgorithm.AStar, SideViewBudget).Should().Be(NavigationGuideStatus.Success);
        Frame();
        Frame();
        host.Pose.Center.X.Should().Be(Fixed64.Quarter);
        host.TryReplaceNavigationProfile(Posture(Fixed64.Zero)).Should().Be(NavigationProfileReplacementStatus.Applied);
        var center = host.Pose.Center;
        host.SupportSample = new NavigationSupport2D(new Vector2d((Fixed64)10, -Fixed64.Half), Vector2d.Forward, Fixed64.One);
        host.RefreshSupport();
        Frame();
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.InvalidStart);
        host.Pose.Center.Should().Be(center);

        void Frame()
        {
            context.Simulate();
            host.PrepareFrameMotion();
            host.CommitFrameMotion();
            context.LateSimulate();
        }
    }

    [Fact]
    public void TopDownReplacementKeepsCenterAndRejectsAnUncoveredFootprint()
    {
        using var context = Scene(NavigationMode2D.TopDown);
        var initial = TopDownProfile(Fixed64.Quarter);
        using var host = new PostureHost(context, initial: initial, tuning: default(NavigationMotorSettings2D));
        host.TryReplaceNavigationProfile(TopDownProfile(Fixed64.FromFraction(3, 8)))
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        host.Pose.Center.Should().Be(Vector2d.Zero);
        host.TryReplaceNavigationProfile(TopDownProfile(Fixed64.FromFraction(3, 4)))
            .Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);
        host.HostCalls.Should().Be(1);

        static NavigationAgentProfile2D TopDownProfile(Fixed64 radius) => new(NavigationMode2D.TopDown, radius,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid,
            TraversalCapability.None);
    }

    [Fact]
    public void ReplacementPreservesAscentAndAppliesJumpCapabilityChanges()
    {
        using var context = GuidedStepScene(flat: true);
        var tuning = new NavigationMotorSettings2D(Settings.MoveSpeed, Fixed64.Zero, Settings.Gravity,
            Settings.JumpSpeed, 2);
        using var host = new PostureHost(context, tuning: tuning);
        host.RefreshSupport();
        JumpFrame();
        var velocity = host.Velocity;
        host.TryReplaceNavigationProfile(Posture(Fixed64.Half, TraversalCapability.None))
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        host.Velocity.Should().Be(velocity);
        JumpFrame();
        host.Velocity.Y.Should().BeLessThan(velocity.Y);
        host.TryReplaceNavigationProfile(Profile).Should().Be(NavigationProfileReplacementStatus.Applied);
        JumpFrame();
        host.Velocity.Y.Should().Be(velocity.Y);
        JumpFrame();
        host.Velocity.Y.Should().BeLessThan(velocity.Y);

        void JumpFrame()
        {
            context.Simulate();
            host.PrepareFrameMotion(new NavigationInput2D(null, true));
            host.CommitFrameMotion();
            context.LateSimulate();
        }
    }

    [Fact]
    public void ReplacementRejectsInvalidShellIntentAndUncommittedMotion()
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new PostureHost(context);
        var compact = Posture(Fixed64.Zero);
        var gas = new NavigationAgentProfile2D(Profile.Mode, Profile.Radius, Profile.AxisLength, Profile.MaxStepUp,
            Profile.MaxDropDown, Profile.MinimumSupportNormalDot, Profile.ArrivalRadius, TraversalMedia.Gas,
            Profile.Capabilities);
        host.TryReplaceNavigationProfile(default).Should().Be(NavigationProfileReplacementStatus.InvalidProfile);
        host.TryReplaceNavigationProfile(gas).Should().Be(NavigationProfileReplacementStatus.CurrentMediumNotAllowed);
        host.RefreshSupport();
        host.ApplySideViewGuidedTrekRequest(GuidedAddress(0), default, SideViewPolicy,
            PathAlgorithm.AStar, SideViewBudget).Should().Be(NavigationGuideStatus.InvalidEnd);
        host.TryReplaceNavigationProfile(compact).Should().Be(NavigationProfileReplacementStatus.GuidanceIncompatible);
        host.CancelGuidance();
        context.Simulate();
        host.PrepareFrameMotion();
        host.TryReplaceNavigationProfile(compact).Should().Be(NavigationProfileReplacementStatus.UnsafeLifecycle);
        host.CommitFrameMotion();
        context.LateSimulate();
        host.HostCalls.Should().Be(0);

        using var extreme = new PostureHost(context, center: new Vector2d(Fixed64.Zero, Fixed64.MinValue),
            identity: new Guid("91851d62-c89b-4e8e-9e7d-cfc7306939b5"));
        extreme.TryReplaceNavigationProfile(compact).Should().Be(NavigationProfileReplacementStatus.FootAnchorUnrepresentable);
    }

    private static NavigationAgentProfile2D Posture(Fixed64 axis, TraversalCapability capabilities = TraversalCapability.Jump) =>
        new(NavigationMode2D.SideView, Profile.Radius, axis, Profile.MaxStepUp, Profile.MaxDropDown,
            Profile.MinimumSupportNormalDot, Profile.ArrivalRadius, TraversalMedia.Solid, capabilities);

    private sealed record OccupancyFixture(Guid GlobalId, Vector3d Position) : IVoxelOccupant
    {
        public byte OccupantGroupId => 0;
    }

    private sealed class PostureHost(TrailblazerWorldContext context, Vector2d center = default,
        NavigationAgentProfile2D? initial = null, NavigationMotorSettings2D? tuning = null, Guid? identity = null)
        : Navigator2D(context, "side", identity ?? new Guid("91851d62-c89b-4e8e-9e7d-cfc7306939b4"),
            new NavigationPose2D(center, Fixed64.Zero), initial ?? Profile, tuning ?? Settings)
    {
        internal NavigationAgentProfile2D HostProfile { get; private set; } = initial ?? Profile;
        internal Vector2d HostCenter { get; private set; } = center;
        internal bool AcceptReplacement { get; set; } = true;
        internal int HostCalls { get; private set; }
        internal Exception? HostFailure { get; set; }
        internal Action? LastPublisher { get; private set; }
        internal bool SkipPublisher { get; set; }
        internal NavigationSupport2D SupportSample { get; set; } = Ground;

        protected override void SubmitMotionTarget(in NavigationMotionRequest2D request) { }

        protected override NavigationMotionResult2D ReadAcceptedMotion(NavigationMotionRequest2D? request)
        {
            if (request is NavigationMotionRequest2D motion)
                HostCenter = motion.Target.Center;
            var pose = new NavigationPose2D(HostCenter, Fixed64.Zero);
            return HostProfile.Mode == NavigationMode2D.TopDown
                ? new NavigationMotionResult2D(pose, NavigationSupportStatus2D.NotApplicable)
                : new NavigationMotionResult2D(pose, NavigationSupportStatus2D.Found, SupportSample);
        }

        protected override bool TryCommitPhysicalProfileReplacement(NavigationAgentProfile2D replacement,
            Vector2d center, Action publishNavigationState, out Exception? notificationException)
        {
            HostCalls++;
            notificationException = null;
            if (!AcceptReplacement)
                return false;
            HostProfile = replacement;
            HostCenter = center;
            LastPublisher = publishNavigationState;
            if (!SkipPublisher)
                publishNavigationState();
            notificationException = HostFailure;
            return true;
        }
    }
}
