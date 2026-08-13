using System;
using System.Collections.Generic;
using FluentAssertions;
using SwiftCollections;
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
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();
        var request = new TestPathRequest(7);

        cache.TryGetOrCreate(request, () => FakeSurveyResult.Create(7), out FakeSurveyResult created).Should().BeTrue();
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
            return FakeSurveyResult.Create(7);
        }, out FakeSurveyResult reused).Should().BeTrue();

        createCalled.Should().BeFalse();
        reused.Should().BeSameAs(created);
        reused.IsInUse.Should().BeTrue();
        cache.CountInUse.Should().Be(1);

        cache.Return(reused, dispose: false);
        cache.CountInUse.Should().Be(0);
        reused.IsInUse.Should().BeFalse();
    }

    [Fact]
    public void SharedCachedResult_ShouldRemainInUseUntilEveryCheckoutIsReturned()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();
        var request = new TestPathRequest(7);

        cache.TryGetOrCreate(request, () => FakeSurveyResult.Create(7), out FakeSurveyResult first)
            .Should()
            .BeTrue();
        cache.TryGetOrCreate(request, () => FakeSurveyResult.Create(7), out FakeSurveyResult second)
            .Should()
            .BeTrue();

        second.Should().BeSameAs(first);
        cache.CountInUse.Should().Be(2);

        cache.Return(first, dispose: false);

        cache.CountInUse.Should().Be(1);
        second.IsInUse.Should().BeTrue();

        cache.EvictStaleEntries(currentFrame: 10_000, expiration: 0);
        cache.Count.Should().Be(1);

        cache.Return(second, dispose: false);

        cache.CountInUse.Should().Be(0);
        second.IsInUse.Should().BeFalse();
    }

    [Fact]
    public void InvalidateForChart_ShouldClearEveryCheckoutOfSharedResult()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();
        var request = new TestPathRequest(7);

        cache.TryGetOrCreate(
                request,
                () => FakeSurveyResult.Create(7, chartsUtilized: new[] { "chart-a" }),
                out FakeSurveyResult first)
            .Should()
            .BeTrue();
        cache.TryGetOrCreate(request, () => FakeSurveyResult.Create(7), out FakeSurveyResult second)
            .Should()
            .BeTrue();
        cache.CountInUse.Should().Be(2);

        cache.InvalidateForChart("chart-a");

        cache.Count.Should().Be(0);
        cache.CountInUse.Should().Be(0);
        second.Should().BeSameAs(first);
        second.IsInUse.Should().BeFalse();
        second.IsValid.Should().BeFalse();

        cache.Return(first, dispose: false);
        cache.Return(second, dispose: false);
        cache.CountInUse.Should().Be(0);
    }

    [Fact]
    public void InvalidateForChart_ShouldResetActiveUncachedResult()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();
        var request = new TestPathRequest(7);

        cache.TryCreateUncached(
                request,
                () => FakeSurveyResult.Create(7, chartsUtilized: new[] { "chart-a" }),
                out FakeSurveyResult result)
            .Should()
            .BeTrue();
        cache.Count.Should().Be(0);
        cache.CountInUse.Should().Be(1);

        cache.InvalidateForChart("chart-a");

        cache.Count.Should().Be(0);
        cache.CountInUse.Should().Be(0);
        result.IsInUse.Should().BeFalse();
        result.IsValid.Should().BeFalse();
        result.ResetCount.Should().Be(1);
    }

    [Fact]
    public void DisposingOneSharedCheckout_ShouldTrackRemainingDetachedResultForInvalidation()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();
        var request = new TestPathRequest(7);

        cache.TryGetOrCreate(
                request,
                () => FakeSurveyResult.Create(7, chartsUtilized: new[] { "chart-a" }),
                out FakeSurveyResult first)
            .Should()
            .BeTrue();
        cache.TryGetOrCreate(request, () => FakeSurveyResult.Create(7), out FakeSurveyResult second)
            .Should()
            .BeTrue();

        cache.Return(first, dispose: true);

        cache.Count.Should().Be(0);
        cache.CountInUse.Should().Be(1);
        second.IsInUse.Should().BeTrue();

        cache.InvalidateForChart("chart-a");

        cache.CountInUse.Should().Be(0);
        second.IsInUse.Should().BeFalse();
        second.IsValid.Should().BeFalse();
    }

    [Fact]
    public void TryGetOrCreate_ShouldEvictLeastRecentlyUsedReusableEntry_WhenCacheIsFull()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();

        for (int i = 0; i < 128; i++)
        {
            cache.TryGetOrCreate(new TestPathRequest(i), () => FakeSurveyResult.Create(i), out FakeSurveyResult result)
                .Should()
                .BeTrue();

            cache.Return(result, dispose: false);
            TestWorld.Context.Simulate();
        }

        cache.Count.Should().Be(128);
        cache.CountInUse.Should().Be(0);

        cache.TryGetOrCreate(new TestPathRequest(999), () => FakeSurveyResult.Create(999), out FakeSurveyResult added)
            .Should()
            .BeTrue();
        cache.Return(added, dispose: false);

        cache.Count.Should().Be(128);

        bool recreatedEvictedEntry = false;
        cache.TryGetOrCreate(new TestPathRequest(0), () =>
        {
            recreatedEvictedEntry = true;
            return FakeSurveyResult.Create(0);
        }, out FakeSurveyResult rehydrated).Should().BeTrue();

        recreatedEvictedEntry.Should().BeTrue();
        rehydrated.RequestCacheKey.Should().Be(TestPathRequest.CreateCacheKey(0));
    }

    [Fact]
    public void TryGetOrCreate_ShouldEvictLeastRecentlyReleasedEntry_WithoutLinqAllocation()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();

        for (int i = 0; i < 128; i++)
        {
            cache.TryGetOrCreate(new TestPathRequest(i), () => FakeSurveyResult.Create(i), out FakeSurveyResult result)
                .Should()
                .BeTrue();

            cache.Return(result, dispose: false);
            TestWorld.Context.Simulate();
        }

        cache.TryGetOrCreate(new TestPathRequest(0), () => FakeSurveyResult.Create(0), out FakeSurveyResult refreshed)
            .Should()
            .BeTrue();
        cache.Return(refreshed, dispose: false);
        TestWorld.Context.Simulate();

        var evictionRequest = new TestPathRequest(999);
        Func<FakeSurveyResult> createEvictionResult = static () => FakeSurveyResult.Create(999);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool created = cache.TryGetOrCreate(evictionRequest, createEvictionResult, out FakeSurveyResult added);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        created.Should().BeTrue();
        allocated.Should().BeLessThan(4_096);
        cache.Return(added, dispose: false);

        bool recreatedOldestEntry = false;
        cache.TryGetOrCreate(new TestPathRequest(1), () =>
        {
            recreatedOldestEntry = true;
            return FakeSurveyResult.Create(1);
        }, out FakeSurveyResult rehydratedOldest).Should().BeTrue();

        recreatedOldestEntry.Should().BeTrue();
        rehydratedOldest.RequestCacheKey.Should().Be(TestPathRequest.CreateCacheKey(1));

        bool recreatedRefreshedEntry = false;
        cache.TryGetOrCreate(new TestPathRequest(0), () =>
        {
            recreatedRefreshedEntry = true;
            return FakeSurveyResult.Create(0);
        }, out FakeSurveyResult rehydratedRefreshed).Should().BeTrue();

        recreatedRefreshedEntry.Should().BeFalse();
        rehydratedRefreshed.Should().BeSameAs(refreshed);

        cache.Return(rehydratedOldest, dispose: false);
        cache.Return(rehydratedRefreshed, dispose: false);
    }

    [Fact]
    public void InvalidateForChart_ShouldUseChartIndexAndMaintainMultiChartMembership()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();
        List<FakeSurveyResult> results = new(128);

        for (int i = 0; i < 128; i++)
        {
            cache.TryGetOrCreate(
                    new TestPathRequest(i),
                    () => FakeSurveyResult.Create(i, chartsUtilized: new[] { "chart-a", "chart-b" }),
                    out FakeSurveyResult result)
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

        SwiftDictionary<string, SwiftList<PathRequestCacheKey>> chartIndex =
            ReflectionUtility.GetPrivateField<SwiftDictionary<string, SwiftList<PathRequestCacheKey>>>(cache, "_chartIndex");
        chartIndex.Count.Should().Be(0);

        var danglingKeys = new SwiftList<PathRequestCacheKey>(1);
        danglingKeys.Add(TestPathRequest.CreateCacheKey(404));
        chartIndex["dangling-chart"] = danglingKeys;

        cache.InvalidateForChart("dangling-chart");

        chartIndex.ContainsKey("dangling-chart").Should().BeFalse();
    }

    [Fact]
    public void TrySeed_ShouldPopulateChartIndexAndTrackCheckedOutEntries()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();

        FakeSurveyResult released = FakeSurveyResult.Create(
            10,
            chartsUtilized: new[] { "chart-a", "chart-b", "chart-a" });
        FakeSurveyResult active = FakeSurveyResult.Create(
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
    public void TrySeed_ShouldRejectInvalidInputs_ReplaceExistingEntries_AndRespectCapacity()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();

        FakeSurveyResult missingPath = FakeSurveyResult.Create(1, hasPath: false);
        FakeSurveyResult missingContext = FakeSurveyResult.Create(2);
        missingContext.Context = null;

        cache.TrySeed(null!, checkout: false).Should().BeFalse();
        cache.TrySeed(missingPath, checkout: false).Should().BeFalse();
        cache.TrySeed(FakeSurveyResult.Create(default(PathRequestCacheKey)), checkout: false).Should().BeFalse();
        cache.TrySeed(missingContext, checkout: false).Should().BeFalse();
        cache.CountIndexedEntriesForChart(string.Empty).Should().Be(0);

        FakeSurveyResult active = FakeSurveyResult.Create(10, chartsUtilized: new[] { "old-chart" });
        cache.TrySeed(active, checkout: true).Should().BeTrue();
        cache.CountInUse.Should().Be(1);

        FakeSurveyResult replacement = FakeSurveyResult.Create(10, chartsUtilized: new[] { "new-chart" });
        cache.TrySeed(replacement, checkout: false).Should().BeTrue();

        active.ResetCount.Should().Be(1);
        cache.Count.Should().Be(1);
        cache.CountInUse.Should().Be(0);
        cache.CountIndexedEntriesForChart("old-chart").Should().Be(0);
        cache.CountIndexedEntriesForChart("new-chart").Should().Be(1);

        replacement.Checkout();
        cache.TrySeed(replacement, checkout: false).Should().BeTrue();
        replacement.IsInUse.Should().BeFalse();

        FakeSurveyResult directReplacement = FakeSurveyResult.Create(10, chartsUtilized: new[] { "direct-chart" });
        ReflectionUtility.InvokePrivate<object?>(
            cache,
            "AddCachedResult",
            TestPathRequest.CreateCacheKey(10),
            directReplacement);
        cache.CountIndexedEntriesForChart("new-chart").Should().Be(0);
        cache.CountIndexedEntriesForChart("direct-chart").Should().Be(1);

        using var fullCache = new ReusableSurveyResultCache<FakeSurveyResult>();
        for (int i = 0; i < 128; i++)
            fullCache.TrySeed(FakeSurveyResult.Create(i), checkout: false).Should().BeTrue();

        fullCache.TrySeed(FakeSurveyResult.Create(500), checkout: false).Should().BeFalse();
        fullCache.Count.Should().Be(128);
    }

    [Fact]
    public void EvictStaleEntries_ShouldNotAllocate_WhenNoEntriesAreStale()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();

        for (int i = 0; i < 32; i++)
        {
            cache.TryGetOrCreate(new TestPathRequest(i), () => FakeSurveyResult.Create(i), out FakeSurveyResult result)
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
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();

        for (int i = 0; i < 32; i++)
        {
            cache.TryGetOrCreate(new TestPathRequest(i), () => FakeSurveyResult.Create(i), out FakeSurveyResult result)
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
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();
        List<FakeSurveyResult> checkedOut = new(128);

        for (int i = 0; i < 128; i++)
        {
            cache.TryGetOrCreate(new TestPathRequest(i), () => FakeSurveyResult.Create(i), out FakeSurveyResult result)
                .Should()
                .BeTrue();

            checkedOut.Add(result);
        }

        cache.Count.Should().Be(128);
        cache.CountInUse.Should().Be(128);

        cache.TryGetOrCreate(new TestPathRequest(2000), () => FakeSurveyResult.Create(2000), out FakeSurveyResult uncached)
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
            return FakeSurveyResult.Create(2000);
        }, out FakeSurveyResult recreatedResult).Should().BeTrue();
        recreated.Should().BeTrue();

        cache.Return(recreatedResult, dispose: false);

        for (int i = 0; i < checkedOut.Count; i++)
            cache.Return(checkedOut[i], dispose: false);

        cache.CountInUse.Should().Be(0);
    }

    [Fact]
    public void CacheOverflowResult_ShouldRemainTrackedForChartInvalidation()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();
        List<FakeSurveyResult> checkedOut = new(128);

        for (int i = 0; i < 128; i++)
        {
            cache.TryGetOrCreate(new TestPathRequest(i), () => FakeSurveyResult.Create(i), out FakeSurveyResult result)
                .Should()
                .BeTrue();
            checkedOut.Add(result);
        }

        cache.TryGetOrCreate(
                new TestPathRequest(2000),
                () => FakeSurveyResult.Create(2000, chartsUtilized: new[] { "overflow-chart" }),
                out FakeSurveyResult overflow)
            .Should()
            .BeTrue();
        cache.Count.Should().Be(128);
        cache.CountInUse.Should().Be(129);

        cache.InvalidateForChart("overflow-chart");

        cache.CountInUse.Should().Be(128);
        overflow.IsInUse.Should().BeFalse();
        overflow.IsValid.Should().BeFalse();

        for (int i = 0; i < checkedOut.Count; i++)
            cache.Return(checkedOut[i], dispose: false);

        cache.CountInUse.Should().Be(0);
    }

    [Fact]
    public void FailedSeed_ShouldPreserveActiveUncachedTracking()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();

        for (int i = 0; i < 128; i++)
            cache.TrySeed(FakeSurveyResult.Create(i), checkout: false).Should().BeTrue();

        var request = new TestPathRequest(500);
        cache.TryCreateUncached(
                request,
                () => FakeSurveyResult.Create(500, chartsUtilized: new[] { "uncached-chart" }),
                out FakeSurveyResult uncached)
            .Should()
            .BeTrue();

        cache.TrySeed(uncached, checkout: false).Should().BeFalse();
        cache.CountInUse.Should().Be(1);
        uncached.IsInUse.Should().BeTrue();

        cache.InvalidateForChart("uncached-chart");

        cache.CountInUse.Should().Be(0);
        uncached.IsInUse.Should().BeFalse();
        uncached.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Cache_ShouldRejectPathlessResults_AndResetInvalidatedEntries()
    {
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();

        cache.InvalidateWhere(_ => true);
        cache.Count.Should().Be(0);

        cache.TryGetOrCreate(
            new TestPathRequest(1),
            () => FakeSurveyResult.Create(1, hasPath: false),
            out FakeSurveyResult failed).Should().BeFalse();

        Assert.NotNull(failed);
        cache.Count.Should().Be(0);
        cache.CountInUse.Should().Be(0);

        cache.TryGetOrCreate(new TestPathRequest(10), () => FakeSurveyResult.Create(10), out FakeSurveyResult inUse)
            .Should()
            .BeTrue();
        cache.TryGetOrCreate(new TestPathRequest(11), () => FakeSurveyResult.Create(11), out FakeSurveyResult pooled)
            .Should()
            .BeTrue();
        cache.Return(pooled, dispose: false);

        cache.InvalidateWhere(result => result.RequestCacheKey.IsInitialized);

        cache.Count.Should().Be(0);
        cache.CountInUse.Should().Be(0);
        inUse.ResetCount.Should().Be(1);
        pooled.ResetCount.Should().Be(1);
        inUse.IsValid.Should().BeFalse();
        pooled.IsValid.Should().BeFalse();
    }

}
