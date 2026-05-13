using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Collections.Generic;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class ReusableSurveyResultCacheTests : IDisposable
{
    public ReusableSurveyResultCacheTests()
    {
        PathManager.Reset();
        TestWorld.Context.Reset();
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryGetOrCreate_ShouldCheckoutAndReleaseCachedHits()
    {
        using var cache = new ReusableSurveyResultCache<TestSurveyResult>();
        var request = new TestPathRequest(7);

        cache.TryGetOrCreate(request, () => TestSurveyResult.Create(7), out TestSurveyResult created).Should().BeTrue();
        cache.Count.Should().Be(1);
        cache.CountInUse.Should().Be(1);
        created.IsInUse.Should().BeTrue();

        cache.Return(created, dispose: false);
        cache.CountInUse.Should().Be(0);
        created.IsInUse.Should().BeFalse();

        bool createCalled = false;
        cache.TryGetOrCreate(request, () =>
        {
            createCalled = true;
            return TestSurveyResult.Create(7);
        }, out TestSurveyResult reused).Should().BeTrue();

        createCalled.Should().BeFalse();
        reused.Should().BeSameAs(created);
        reused.IsInUse.Should().BeTrue();
        cache.CountInUse.Should().Be(1);

        cache.Return(reused, dispose: false);
        cache.CountInUse.Should().Be(0);
        reused.IsInUse.Should().BeFalse();
    }

    [Fact]
    public void TryGetOrCreate_ShouldEvictLeastRecentlyUsedReusableEntry_WhenCacheIsFull()
    {
        using var cache = new ReusableSurveyResultCache<TestSurveyResult>();

        for (int i = 0; i < 128; i++)
        {
            cache.TryGetOrCreate(new TestPathRequest(i), () => TestSurveyResult.Create(i), out TestSurveyResult result)
                .Should()
                .BeTrue();

            cache.Return(result, dispose: false);
            TestWorld.Context.Simulate();
        }

        cache.Count.Should().Be(128);
        cache.CountInUse.Should().Be(0);

        cache.TryGetOrCreate(new TestPathRequest(999), () => TestSurveyResult.Create(999), out TestSurveyResult added)
            .Should()
            .BeTrue();
        cache.Return(added, dispose: false);

        cache.Count.Should().Be(128);

        bool recreatedEvictedEntry = false;
        cache.TryGetOrCreate(new TestPathRequest(0), () =>
        {
            recreatedEvictedEntry = true;
            return TestSurveyResult.Create(0);
        }, out TestSurveyResult rehydrated).Should().BeTrue();

        recreatedEvictedEntry.Should().BeTrue();
        rehydrated.RequestHashKey.Should().Be(0);
    }

    [Fact]
    public void TryGetOrCreate_ShouldEvictLeastRecentlyReleasedEntry_WithoutLinqAllocation()
    {
        using var cache = new ReusableSurveyResultCache<TestSurveyResult>();

        for (int i = 0; i < 128; i++)
        {
            cache.TryGetOrCreate(new TestPathRequest(i), () => TestSurveyResult.Create(i), out TestSurveyResult result)
                .Should()
                .BeTrue();

            cache.Return(result, dispose: false);
            TestWorld.Context.Simulate();
        }

        cache.TryGetOrCreate(new TestPathRequest(0), () => TestSurveyResult.Create(0), out TestSurveyResult refreshed)
            .Should()
            .BeTrue();
        cache.Return(refreshed, dispose: false);
        TestWorld.Context.Simulate();

        var evictionRequest = new TestPathRequest(999);
        Func<TestSurveyResult> createEvictionResult = static () => TestSurveyResult.Create(999);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool created = cache.TryGetOrCreate(evictionRequest, createEvictionResult, out TestSurveyResult added);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        created.Should().BeTrue();
        allocated.Should().BeLessThan(4_096);
        cache.Return(added, dispose: false);

        bool recreatedOldestEntry = false;
        cache.TryGetOrCreate(new TestPathRequest(1), () =>
        {
            recreatedOldestEntry = true;
            return TestSurveyResult.Create(1);
        }, out TestSurveyResult rehydratedOldest).Should().BeTrue();

        recreatedOldestEntry.Should().BeTrue();
        rehydratedOldest.RequestHashKey.Should().Be(1);

        bool recreatedRefreshedEntry = false;
        cache.TryGetOrCreate(new TestPathRequest(0), () =>
        {
            recreatedRefreshedEntry = true;
            return TestSurveyResult.Create(0);
        }, out TestSurveyResult rehydratedRefreshed).Should().BeTrue();

        recreatedRefreshedEntry.Should().BeFalse();
        rehydratedRefreshed.Should().BeSameAs(refreshed);

        cache.Return(rehydratedOldest, dispose: false);
        cache.Return(rehydratedRefreshed, dispose: false);
    }

    [Fact]
    public void InvalidateForChart_ShouldUseChartIndexAndMaintainMultiChartMembership()
    {
        using var cache = new ReusableSurveyResultCache<TestSurveyResult>();
        List<TestSurveyResult> results = new(128);

        for (int i = 0; i < 128; i++)
        {
            cache.TryGetOrCreate(
                    new TestPathRequest(i),
                    () => TestSurveyResult.Create(i, chartsUtilized: new[] { "chart-a", "chart-b" }),
                    out TestSurveyResult result)
                .Should()
                .BeTrue();

            cache.Return(result, dispose: false);
            results.Add(result);
        }

        cache.InvalidateForChart("missing-chart");

        cache.Count.Should().Be(128);
        results.Should().OnlyContain(result => result.ResetCount == 0);

        cache.InvalidateForChart("chart-a");

        cache.Count.Should().Be(0);
        cache.CountInUse.Should().Be(0);
        results.Should().OnlyContain(result => result.ResetCount == 1);

        SwiftDictionary<string, SwiftList<int>> chartIndex =
            ReflectionUtility.GetPrivateField<SwiftDictionary<string, SwiftList<int>>>(cache, "_chartIndex");
        chartIndex.Count.Should().Be(0);
    }

    [Fact]
    public void TrySeed_ShouldPopulateChartIndexAndTrackCheckedOutEntries()
    {
        using var cache = new ReusableSurveyResultCache<TestSurveyResult>();

        TestSurveyResult released = TestSurveyResult.Create(
            10,
            chartsUtilized: new[] { "chart-a", "chart-b", "chart-a" });
        TestSurveyResult active = TestSurveyResult.Create(
            11,
            chartsUtilized: new[] { "chart-a" });

        cache.TrySeed(released, checkout: false).Should().BeTrue();
        cache.TrySeed(active, checkout: true).Should().BeTrue();

        cache.Count.Should().Be(2);
        cache.CountInUse.Should().Be(1);
        released.IsInUse.Should().BeFalse();
        active.IsInUse.Should().BeTrue();
        cache.CountIndexedEntriesForChart("chart-a").Should().Be(2);
        cache.CountIndexedEntriesForChart("chart-b").Should().Be(1);

        cache.InvalidateForChart("chart-a");

        cache.Count.Should().Be(0);
        cache.CountInUse.Should().Be(0);
        released.ResetCount.Should().Be(1);
        active.ResetCount.Should().Be(1);
    }

    [Fact]
    public void EvictStaleEntries_ShouldNotAllocate_WhenNoEntriesAreStale()
    {
        using var cache = new ReusableSurveyResultCache<TestSurveyResult>();

        for (int i = 0; i < 32; i++)
        {
            cache.TryGetOrCreate(new TestPathRequest(i), () => TestSurveyResult.Create(i), out TestSurveyResult result)
                .Should()
                .BeTrue();

            cache.Return(result, dispose: false);
        }

        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() => cache.EvictStaleEntries(currentFrame: 0, expiration: 600));

        allocated.Should().BeLessThan(64);
        cache.Count.Should().Be(32);
    }

    [Fact]
    public void EvictStaleEntries_ShouldNotAllocate_WhenEntriesAreStale()
    {
        using var cache = new ReusableSurveyResultCache<TestSurveyResult>();

        for (int i = 0; i < 32; i++)
        {
            cache.TryGetOrCreate(new TestPathRequest(i), () => TestSurveyResult.Create(i), out TestSurveyResult result)
                .Should()
                .BeTrue();

            cache.Return(result, dispose: false);
        }

        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() => cache.EvictStaleEntries(currentFrame: 10_000, expiration: 600));

        allocated.Should().BeLessThan(128);
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void TryGetOrCreate_ShouldReturnUncachedResult_WhenCacheIsFullAndAllEntriesAreInUse()
    {
        using var cache = new ReusableSurveyResultCache<TestSurveyResult>();
        List<TestSurveyResult> checkedOut = new(128);

        for (int i = 0; i < 128; i++)
        {
            cache.TryGetOrCreate(new TestPathRequest(i), () => TestSurveyResult.Create(i), out TestSurveyResult result)
                .Should()
                .BeTrue();

            checkedOut.Add(result);
        }

        cache.Count.Should().Be(128);
        cache.CountInUse.Should().Be(128);

        cache.TryGetOrCreate(new TestPathRequest(2000), () => TestSurveyResult.Create(2000), out TestSurveyResult uncached)
            .Should()
            .BeTrue();

        cache.Count.Should().Be(128);
        cache.CountInUse.Should().Be(129);
        uncached.IsInUse.Should().BeTrue();

        cache.Return(uncached, dispose: false);
        cache.Count.Should().Be(128);
        cache.CountInUse.Should().Be(128);

        bool recreated = false;
        cache.TryGetOrCreate(new TestPathRequest(2000), () =>
        {
            recreated = true;
            return TestSurveyResult.Create(2000);
        }, out TestSurveyResult recreatedResult).Should().BeTrue();
        recreated.Should().BeTrue();

        cache.Return(recreatedResult, dispose: false);

        for (int i = 0; i < checkedOut.Count; i++)
            cache.Return(checkedOut[i], dispose: false);

        cache.CountInUse.Should().Be(0);
    }

    [Fact]
    public void Cache_ShouldRejectPathlessResults_AndResetInvalidatedEntries()
    {
        using var cache = new ReusableSurveyResultCache<TestSurveyResult>();

        cache.TryGetOrCreate(
            new TestPathRequest(1),
            () => TestSurveyResult.Create(1, hasPath: false),
            out TestSurveyResult failed).Should().BeFalse();

        Assert.NotNull(failed);
        cache.Count.Should().Be(0);
        cache.CountInUse.Should().Be(0);

        cache.TryGetOrCreate(new TestPathRequest(10), () => TestSurveyResult.Create(10), out TestSurveyResult inUse)
            .Should()
            .BeTrue();
        cache.TryGetOrCreate(new TestPathRequest(11), () => TestSurveyResult.Create(11), out TestSurveyResult pooled)
            .Should()
            .BeTrue();
        cache.Return(pooled, dispose: false);

        cache.InvalidateWhere(result => result.RequestHashKey >= 10);

        cache.Count.Should().Be(0);
        cache.CountInUse.Should().Be(0);
        inUse.ResetCount.Should().Be(1);
        pooled.ResetCount.Should().Be(1);
        inUse.IsValid.Should().BeFalse();
        pooled.IsValid.Should().BeFalse();
    }

    private sealed class TestPathRequest : IPathRequest
    {
        public TestPathRequest(int key)
        {
            RequestCacheKey = key;
        }

        public TrailblazerWorldContext Context => TestWorld.Context;

        public Vector3d Origin => Vector3d.Zero;

        public Voxel StartNode => null!;

        public Vector3d TargetPosition => Vector3d.Zero;

        public Voxel EndNode => null!;

        public Fixed64 UnitSize => Fixed64.One;

        public bool HasZeroDisplacement => false;

        public bool AllowUnwalkableEndpoints => false;

        public int MaxPathSearchRange { get; set; } = 1;

        public bool HasOrigin => true;

        public bool HasDestination => true;

        public bool HasValidEndpoints => true;

        public bool IsValid => true;

        public int RequestCacheKey { get; }

        public bool UpdateRequest(Vector3d origin, Vector3d destination, Fixed64? unitSize) => false;

        public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false) => false;

        public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false) => false;

        public bool TrySetUnitSize(Fixed64 unitSize) => false;
    }

    private sealed class TestSurveyResult : SurveyResult
    {
        private readonly bool _hasPath;

        private TestSurveyResult(int key, bool hasPath, string[]? chartsUtilized)
        {
            _hasPath = hasPath;
            IsValid = hasPath;
            RequestHashKey = key;
            LastUsedFrame = -1;
            ChartsUtilized = chartsUtilized ?? Array.Empty<string>();
        }

        public int ResetCount { get; private set; }

        public override bool HasPath => IsValid && _hasPath;

        public static TestSurveyResult Create(
            int key,
            bool hasPath = true,
            string[]? chartsUtilized = null)
            => new(key, hasPath, chartsUtilized)
            {
                Context = TestWorld.Context
            };

        public override void Reset()
        {
            ResetCount++;
            base.Reset();
        }
    }
}
