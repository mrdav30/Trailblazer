using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
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
        TrailblazerManager.Reset();
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerManager.Reset();
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
            TrailblazerManager.Simulate();
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

        failed.Should().NotBeNull();
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

        private TestSurveyResult(int key, bool hasPath)
        {
            _hasPath = hasPath;
            IsValid = hasPath;
            RequestHashKey = key;
            LastUsedFrame = -1;
            ChartsUtilized = Array.Empty<string>();
        }

        public int ResetCount { get; private set; }

        public override bool HasPath => IsValid && _hasPath;

        public static TestSurveyResult Create(int key, bool hasPath = true) => new(key, hasPath);

        public override void Reset()
        {
            ResetCount++;
            base.Reset();
        }
    }
}
