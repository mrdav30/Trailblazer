using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using SwiftCollections.Diagnostics;
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
