using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using SwiftCollections;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class PathGuideFactoryCoverageTests : IDisposable
{
    public PathGuideFactoryCoverageTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)), out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TraversalTransitionRegistry.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RequestGuide_ShouldRejectNullInvalidAndUnknownRequests()
    {
        PathGuideFactory.RequestGuide(null!, out IGuide? nullGuide).Should().BeFalse();
        nullGuide.Should().BeNull();

        var invalidRequest = new TestPathRequest { IsValid = false };
        PathGuideFactory.RequestGuide(invalidRequest, out IGuide? invalidGuide).Should().BeFalse();
        invalidGuide.Should().BeNull();

        using TrailblazerWorldContext otherContext = PathTestFactory.CreateContextWithGrid();
        var foreignContextRequest = new TestPathRequest(otherContext) { IsValid = true };
        PathGuideFactory.RequestGuide(foreignContextRequest, out IGuide? foreignGuide).Should().BeFalse();
        foreignGuide.Should().BeNull();

        PathTestFactory.RegisterSolidLine(TestWorld.Context, "GuideFactoryUnknown", Vector3d.Zero, 2);
        Voxel start = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));

        var unknownRequest = new TestPathRequest
        {
            Origin = Vector3d.Zero,
            StartNode = start,
            TargetPosition = new Vector3d(1, 0, 0),
            EndNode = end,
            UnitSize = Fixed64.One,
            MaxPathSearchRange = 1,
            IsValid = true
        };

        PathGuideFactory.RequestGuide(unknownRequest, out IGuide? unknownGuide).Should().BeFalse();
        unknownGuide.Should().BeNull();
    }

    [Fact]
    public void PathGuideFactory_ShouldInvalidateChartAndVolumeCaches()
    {
        PathTestFactory.RegisterVolumeLine(TestWorld.Context, new Vector3d(0, 0, 2), TraversalMedium.Gas, 3, "GuideFactoryInvalidateVolume");
        VolumePathRequest volumeRequest = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas));
        VolumeGuide volumeGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(volumeRequest, out VolumeGuide? createdVolumeGuide),
            createdVolumeGuide);
        PathGuideFactory.ReturnGuide(volumeGuide);
        PathGuideFactory.TotalVolumeGuideCount.Should().Be(1);

        PathGuideFactory.InvalidateVolumeCache();
        PathGuideFactory.TotalVolumeGuideCount.Should().Be(0);
    }

    [Fact]
    public void PathGuideFactory_ShouldReturnFalse_ForDisconnectedVolumeRequests()
    {
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(0, 0, 4), TraversalMedium.Gas, "GuideFactoryVolumeGap");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(2, 0, 4), TraversalMedium.Gas, "GuideFactoryVolumeGap");

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 4),
            new Vector3d(2, 0, 4),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        PathGuideFactory.RequestGuide(request, out VolumeGuide? guide).Should().BeFalse();
        guide.Should().BeNull();
        PathGuideFactory.TotalVolumeGuideCount.Should().Be(0);
    }

    [Fact]
    public void ReusableSurveyResultCacheWarmCheckout_ShouldNotAllocateSteadyState()
    {
        var request = new TestPathRequest { IsValid = true };
        var cache = new ReusableSurveyResultCache<FakeSurveyResult>();

        cache.TryGetOrCreate(request, () => FakeSurveyResult.Create(request.RequestCacheKey), out FakeSurveyResult result).Should().BeTrue();
        cache.Return(result, dispose: false);

        const int iterations = 256;
        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                if (!cache.TryCheckout(request, out FakeSurveyResult warmResult))
                    throw new InvalidOperationException("Expected cache warm hit.");

                cache.Return(warmResult, dispose: false);
            }
        });

        allocated.Should().BeLessThan(1_024);
    }

    [Fact]
    public void GuidePoolRentRelease_ShouldNotAllocateSteadyState()
    {
        var pool = new GuidePool<FakeGuide>(() => new FakeGuide(), static guide => guide.Reset());
        FakeGuide warmGuide = pool.Rent();
        pool.Release(warmGuide);

        const int iterations = 256;
        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                FakeGuide guide = pool.Rent();
                pool.Release(guide);
            }
        });

        allocated.Should().BeLessThan(128);
    }

    private static void RegisterVolumePoint(string chartName, Vector3d position, TraversalMedia media)
    {
        var data = new NavigationChartCell[1, 1, 1];
        data[0, 0, 0] = new NavigationChartCell(media);

        PathManager.Register(NavigationChart.From3D(chartName, data, position, Fixed64.One));
    }

    private static long MeasureWarmHitAllocation<T>(IPathRequest request, int iterations)
        where T : class, IGuide
    {
        T warmGuide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out T? createdWarmGuide), createdWarmGuide);
        PathGuideFactory.ReturnGuide(warmGuide);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            if (!PathGuideFactory.RequestGuide(request, out T? guide) || guide == null)
                throw new InvalidOperationException($"Expected warm {typeof(T).Name} cache hit.");

            PathGuideFactory.ReturnGuide(guide);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

}
