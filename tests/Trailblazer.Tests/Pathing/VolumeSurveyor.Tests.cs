using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Trailblazer.Tests;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class VolumeSurveyorTests : IDisposable
{
    public VolumeSurveyorTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        GlobalGridManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void FindPath_ShouldReturnEmpty_ForNullZeroDisplacementAndInvalidRequests()
    {
        VolumeSurveyor.Shared.FindPath(null).HasPath.Should().BeFalse();

        AddOpen(Vector3d.Zero);

        VolumePathRequest sameVoxel = VolumePathRequest.Create(Vector3d.Zero, Vector3d.Zero, Fixed64.One);
        sameVoxel.Should().NotBeNull();
        VolumeSurveyor.Shared.FindPath(sameVoxel).HasPath.Should().BeFalse();

        VolumePathRequest invalid = VolumePathRequest.Create(Vector3d.Zero, Vector3d.Zero, Fixed64.One);
        invalid.UpdateRequest(new Vector3d(64, 0, 0), Vector3d.Zero, Fixed64.One).Should().BeFalse();
        VolumeSurveyor.Shared.FindPath(invalid).HasPath.Should().BeFalse();
    }

    [Fact]
    public void FindPath_ShouldReturnEmpty_WhenNoRouteExists()
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(2, 0, 0));

        VolumePathRequest request = VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One);

        request.Should().NotBeNull();

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeFalse();
        result.Waypoints.Should().BeNull();
    }

    [Fact]
    public void FindPath_ShouldRejectDiagonalCornerCutting()
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(1, 0, 1));

        VolumePathRequest request = VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(1, 0, 1),
            Fixed64.One);

        request.Should().NotBeNull();

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeFalse();
        result.Waypoints.Should().BeNull();
    }

    private static void AddOpen(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Gas, "VolumeOpen");
    }
}
