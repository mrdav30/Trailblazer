using FixedMathSharp;
using FluentAssertions;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

public sealed class PathRequestCacheKeyTests
{
    [Fact]
    public void Equality_ShouldUseExactWorldIdentity_WhenIntegerHashesCollide()
    {
        WorldVoxelIndex worldAOrigin = CreateIndex(worldToken: 1, gridToken: 2, x: 0);
        WorldVoxelIndex worldADestination = CreateIndex(worldToken: 1, gridToken: 2, x: 1);
        WorldVoxelIndex worldBOrigin = CreateIndex(worldToken: 1L << 32, gridToken: 2, x: 0);
        WorldVoxelIndex worldBDestination = CreateIndex(worldToken: 1L << 32, gridToken: 2, x: 1);

        PathRequestCacheKey worldA = CreateAStarKey(worldAOrigin, worldADestination);
        PathRequestCacheKey worldB = CreateAStarKey(worldBOrigin, worldBDestination);

        worldA.GetHashCode().Should().Be(worldB.GetHashCode());
        worldA.Should().NotBe(worldB);
    }

    [Fact]
    public void Equality_ShouldUseExactGridGeneration_WhenIntegerHashesCollide()
    {
        WorldVoxelIndex firstOrigin = CreateIndex(worldToken: 1, gridToken: 2, x: 0);
        WorldVoxelIndex firstDestination = CreateIndex(worldToken: 1, gridToken: 2, x: 1);
        WorldVoxelIndex replacementOrigin = CreateIndex(worldToken: 1, gridToken: 2L << 32, x: 0);
        WorldVoxelIndex replacementDestination = CreateIndex(worldToken: 1, gridToken: 2L << 32, x: 1);

        PathRequestCacheKey first = CreateAStarKey(firstOrigin, firstDestination);
        PathRequestCacheKey replacement = CreateAStarKey(replacementOrigin, replacementDestination);

        first.GetHashCode().Should().Be(replacement.GetHashCode());
        first.Should().NotBe(replacement);
    }

    [Fact]
    public void Equality_ShouldIncludeRequestOptions_WhenEndpointsMatch()
    {
        WorldVoxelIndex origin = CreateIndex(worldToken: 1, gridToken: 2, x: 0);
        WorldVoxelIndex destination = CreateIndex(worldToken: 1, gridToken: 2, x: 1);

        PathRequestCacheKey walkableOnly = CreateAStarKey(origin, destination);
        PathRequestCacheKey allowUnwalkable = PathRequestCacheKey.CreateAStar(
            origin,
            destination,
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: false,
            HeuristicMethod.Manhattan,
            Fixed64.One,
            maxPathSearchRange: 32,
            transitionRegistryVersion: 0);

        walkableOnly.Should().NotBe(allowUnwalkable);
    }

    [Fact]
    public void HybridEquality_ShouldCompareOrderedTransitionIdsOrdinally()
    {
        WorldVoxelIndex origin = CreateIndex(worldToken: 1, gridToken: 2, x: 0);
        WorldVoxelIndex destination = CreateIndex(worldToken: 1, gridToken: 2, x: 1);
        TraversalTransition first = CreateTransition("alpha", origin, destination);
        TraversalTransition second = CreateTransition("beta", destination, origin);

        PathRequestCacheKey forward = PathRequestCacheKey.CreateHybrid(
            origin,
            destination,
            Fixed64.One,
            HybridChartRequestKind.AStar,
            allowUnwalkableEndpoints: false,
            HeuristicMethod.Manhattan,
            Fixed64.One,
            extraFloodRange: 0,
            maxPathSearchRange: 32,
            new[] { first, second },
            transitionRegistryVersion: 7,
            volumeRulesRegistryVersion: 11);
        PathRequestCacheKey reverse = PathRequestCacheKey.CreateHybrid(
            origin,
            destination,
            Fixed64.One,
            HybridChartRequestKind.AStar,
            allowUnwalkableEndpoints: false,
            HeuristicMethod.Manhattan,
            Fixed64.One,
            extraFloodRange: 0,
            maxPathSearchRange: 32,
            new[] { second, first },
            transitionRegistryVersion: 7,
            volumeRulesRegistryVersion: 11);

        forward.Should().NotBe(reverse);
    }

    [Fact]
    public void RepeatedHybridKeyReads_ShouldNotAllocate()
    {
        WorldVoxelIndex origin = CreateIndex(worldToken: 1, gridToken: 2, x: 0);
        WorldVoxelIndex destination = CreateIndex(worldToken: 1, gridToken: 2, x: 1);
        TraversalTransition[] transitions =
        {
            CreateTransition("alpha", origin, destination),
            CreateTransition("beta", destination, origin)
        };
        PathRequestCacheKey key = PathRequestCacheKey.CreateHybrid(
            origin,
            destination,
            Fixed64.One,
            HybridChartRequestKind.AStar,
            allowUnwalkableEndpoints: false,
            HeuristicMethod.Manhattan,
            Fixed64.One,
            extraFloodRange: 0,
            maxPathSearchRange: 32,
            transitions,
            transitionRegistryVersion: 7,
            volumeRulesRegistryVersion: 11);
        PathRequestCacheKey equalKey = PathRequestCacheKey.CreateHybrid(
            origin,
            destination,
            Fixed64.One,
            HybridChartRequestKind.AStar,
            allowUnwalkableEndpoints: false,
            HeuristicMethod.Manhattan,
            Fixed64.One,
            extraFloodRange: 0,
            maxPathSearchRange: 32,
            transitions,
            transitionRegistryVersion: 7,
            volumeRulesRegistryVersion: 11);

        long aggregate = 0;
        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < 1_024; i++)
            {
                aggregate += key.GetHashCode();
                aggregate += key.Equals(equalKey) ? 1 : 0;
            }
        });

        aggregate.Should().NotBe(0);
        allocated.Should().Be(0);
    }

    [Fact]
    public void HybridKey_ShouldSnapshotTransitionIds_WhenSourceArrayMutates()
    {
        WorldVoxelIndex origin = CreateIndex(worldToken: 1, gridToken: 2, x: 0);
        WorldVoxelIndex destination = CreateIndex(worldToken: 1, gridToken: 2, x: 1);
        TraversalTransition original = CreateTransition("original", origin, destination);
        TraversalTransition[] transitions = { original };
        PathRequestCacheKey key = PathRequestCacheKey.CreateHybrid(
            origin,
            destination,
            Fixed64.One,
            HybridChartRequestKind.AStar,
            allowUnwalkableEndpoints: false,
            HeuristicMethod.Manhattan,
            Fixed64.One,
            extraFloodRange: 0,
            maxPathSearchRange: 32,
            transitions,
            transitionRegistryVersion: 7,
            volumeRulesRegistryVersion: 11);
        PathRequestCacheKey expected = PathRequestCacheKey.CreateHybrid(
            origin,
            destination,
            Fixed64.One,
            HybridChartRequestKind.AStar,
            allowUnwalkableEndpoints: false,
            HeuristicMethod.Manhattan,
            Fixed64.One,
            extraFloodRange: 0,
            maxPathSearchRange: 32,
            new[] { original },
            transitionRegistryVersion: 7,
            volumeRulesRegistryVersion: 11);
        int originalHash = key.GetHashCode();

        transitions[0] = CreateTransition("replacement", origin, destination);

        key.GetHashCode().Should().Be(originalHash);
        key.Should().Be(expected);
    }

    [Fact]
    public void HybridEquality_ShouldIncludeRouteRegistryVersions()
    {
        WorldVoxelIndex origin = CreateIndex(worldToken: 1, gridToken: 2, x: 0);
        WorldVoxelIndex destination = CreateIndex(worldToken: 1, gridToken: 2, x: 1);
        TraversalTransition[] transitions = { CreateTransition("route", origin, destination) };
        PathRequestCacheKey baseline = CreateHybridKey(
            origin,
            destination,
            transitions,
            transitionRegistryVersion: 7,
            volumeRulesRegistryVersion: 11);
        PathRequestCacheKey changedTransitions = CreateHybridKey(
            origin,
            destination,
            transitions,
            transitionRegistryVersion: 8,
            volumeRulesRegistryVersion: 11);
        PathRequestCacheKey changedVolumeRules = CreateHybridKey(
            origin,
            destination,
            transitions,
            transitionRegistryVersion: 7,
            volumeRulesRegistryVersion: 12);

        baseline.Should().NotBe(changedTransitions);
        baseline.Should().NotBe(changedVolumeRules);
    }

    [Fact]
    public void Cache_ShouldNotAliasDistinctKeysWithEqualIntegerHashes()
    {
        PathRequestCacheKey worldA = CreateAStarKey(
            CreateIndex(worldToken: 1, gridToken: 2, x: 0),
            CreateIndex(worldToken: 1, gridToken: 2, x: 1));
        PathRequestCacheKey worldB = CreateAStarKey(
            CreateIndex(worldToken: 1L << 32, gridToken: 2, x: 0),
            CreateIndex(worldToken: 1L << 32, gridToken: 2, x: 1));
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        using var cache = new ReusableSurveyResultCache<FakeSurveyResult>();

        cache.TryGetOrCreate(
                new TestPathRequest(worldA, context),
                () => FakeSurveyResult.Create(worldA, context: context),
                out FakeSurveyResult first)
            .Should()
            .BeTrue();
        cache.Return(first, dispose: false);

        bool secondCreated = false;
        cache.TryGetOrCreate(
                new TestPathRequest(worldB, context),
                () =>
                {
                    secondCreated = true;
                    return FakeSurveyResult.Create(worldB, context: context);
                },
                out FakeSurveyResult second)
            .Should()
            .BeTrue();

        worldA.GetHashCode().Should().Be(worldB.GetHashCode());
        secondCreated.Should().BeTrue();
        second.Should().NotBeSameAs(first);
        cache.Count.Should().Be(2);
    }

    private static PathRequestCacheKey CreateAStarKey(
        WorldVoxelIndex origin,
        WorldVoxelIndex destination) =>
        PathRequestCacheKey.CreateAStar(
            origin,
            destination,
            Fixed64.One,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            HeuristicMethod.Manhattan,
            Fixed64.One,
            maxPathSearchRange: 32,
            transitionRegistryVersion: 0);

    private static PathRequestCacheKey CreateHybridKey(
        WorldVoxelIndex origin,
        WorldVoxelIndex destination,
        TraversalTransition[] transitions,
        int transitionRegistryVersion,
        int volumeRulesRegistryVersion) =>
        PathRequestCacheKey.CreateHybrid(
            origin,
            destination,
            Fixed64.One,
            HybridChartRequestKind.AStar,
            allowUnwalkableEndpoints: false,
            HeuristicMethod.Manhattan,
            Fixed64.One,
            extraFloodRange: 0,
            maxPathSearchRange: 32,
            transitions,
            transitionRegistryVersion,
            volumeRulesRegistryVersion);

    private static WorldVoxelIndex CreateIndex(long worldToken, long gridToken, int x) =>
        new(worldToken, gridIndex: 0, gridToken, new VoxelIndex(x, 0, 0));

    private static TraversalTransition CreateTransition(
        string id,
        WorldVoxelIndex source,
        WorldVoxelIndex destination) =>
        new(
            id,
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(source),
            TraversalTransitionAnchor.Solid(destination));
}
