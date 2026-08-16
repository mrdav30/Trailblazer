using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class PathRequestCoverageTests : IDisposable
{
    public PathRequestCoverageTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _);

        RegisterGasLine(new Vector3d(0, 0, 2), 3, "VolumeGridZero");
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void VolumePathRequest_ShouldResetSearchRange_AndTrackVersionedHash()
    {
        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        int originalHash = request.GetHashCode();

        request.TrySetOrigin(new Vector3d(1, 0, 2), resetSearchRange: true).Should().BeTrue();
        TestRequire.NotNull(request.StartNode).WorldPosition.Should().Be(new Vector3d(1, 0, 2));
        request.MaxPathSearchRange.Should().BeGreaterThan(0);

        request.TrySetDestination(new Vector3d(1, 0, 2), resetSearchRange: true).Should().BeTrue();
        TestRequire.NotNull(request.StartNode).GridIndex.Should().Be(TestRequire.NotNull(request.EndNode).GridIndex);
        request.MaxPathSearchRange.Should().BeGreaterThan(0);

        request.TrySetUnitSize(Fixed64.One).Should().BeFalse();
        request.TrySetUnitSize(Fixed64.Two).Should().BeTrue();
        request.UnitSize.Should().Be(Fixed64.Two);

        VolumeMediumRules.SetGasVoxelRule(static _ => true);
        request.GetHashCode().Should().NotBe(originalHash);
    }

    [Fact]
    public void VolumePathRequest_ShouldHandleFailedSetters_AndRevalidateUnitSizeChanges()
    {
        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        int originalRange = request.MaxPathSearchRange;

        request.TrySetOrigin(new Vector3d(1, 0, 2)).Should().BeTrue();
        request.MaxPathSearchRange.Should().Be(originalRange);

        request.TrySetDestination(new Vector3d(1, 0, 2)).Should().BeTrue();
        request.MaxPathSearchRange.Should().Be(originalRange);

        request.TrySetOrigin(new Vector3d(-20, 0, 2)).Should().BeFalse();
        request.TrySetDestination(new Vector3d(20, 0, 2)).Should().BeFalse();

        request.UpdateRequest(new Vector3d(-20, 0, 2), new Vector3d(-18, 0, 2), Fixed64.One).Should().BeFalse();
        request.TrySetOrigin(new Vector3d(0, 0, 2)).Should().BeFalse();
        request.TrySetDestination(new Vector3d(2, 0, 2)).Should().BeFalse();

        Vector3d boundaryPoint = new(-4, -4, -4);
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, boundaryPoint, TraversalMedium.Gas, "VolumeUnitSizeSingle");
        VolumePathRequest sizeSensitive = VolumePathRequest.Create(TestWorld.Context, boundaryPoint,
            boundaryPoint,
            Fixed64.One,
            medium: TraversalMedium.Gas)
            ?? throw new InvalidOperationException("Expected valid boundary Volume request.");

        sizeSensitive.TrySetUnitSize(Fixed64.Two).Should().BeFalse();
        sizeSensitive.HasValidEndpoints.Should().BeFalse();
        sizeSensitive.IsValid.Should().BeFalse();
    }

    [Fact]
    public void VolumePathRequest_Equals_ShouldSupportObjectAndTypedOverloads()
    {
        VolumePathRequest a = VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas)
            ?? throw new InvalidOperationException("Expected valid Volume request.");
        VolumePathRequest b = VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas)
            ?? throw new InvalidOperationException("Expected valid Volume request.");
        VolumePathRequest c = VolumePathRequest.Create(TestWorld.Context, new Vector3d(1, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas)
            ?? throw new InvalidOperationException("Expected valid Volume request.");
        VolumePathRequest? missing = null;

        a.Equals((object)b).Should().BeTrue();
        a.Equals((object)c).Should().BeFalse();
        a.Equals(new object()).Should().BeFalse();
        a.Equals(missing).Should().BeFalse();
        a.Equals(b).Should().BeTrue();
        a.Equals(c).Should().BeFalse();
    }

    private static void RegisterGasLine(Vector3d start, int length, string chartNamePrefix)
    {
        for (int i = 0; i < length; i++)
        {
            PathTestFactory.RegisterGeneratedVolumePoint(
                TestWorld.Context, new Vector3d(start.X + i, start.Y, start.Z),
                TraversalMedium.Gas,
                chartNamePrefix);
        }
    }

}
