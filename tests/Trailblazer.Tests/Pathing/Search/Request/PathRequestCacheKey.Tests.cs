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

        PathRequestCacheKey worldA = CreateVolumeKey(worldAOrigin, worldADestination);
        PathRequestCacheKey worldB = CreateVolumeKey(worldBOrigin, worldBDestination);

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

        PathRequestCacheKey first = CreateVolumeKey(firstOrigin, firstDestination);
        PathRequestCacheKey replacement = CreateVolumeKey(replacementOrigin, replacementDestination);

        first.GetHashCode().Should().Be(replacement.GetHashCode());
        first.Should().NotBe(replacement);
    }

    [Fact]
    public void Equality_ShouldIncludeRequestOptions_WhenEndpointsMatch()
    {
        WorldVoxelIndex origin = CreateIndex(worldToken: 1, gridToken: 2, x: 0);
        WorldVoxelIndex destination = CreateIndex(worldToken: 1, gridToken: 2, x: 1);

        PathRequestCacheKey walkableOnly = CreateVolumeKey(origin, destination);
        PathRequestCacheKey allowUnwalkable = PathRequestCacheKey.CreateVolume(
            origin,
            destination,
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            HeuristicMethod.Manhattan,
            TraversalMedium.Gas,
            maxPathSearchRange: 32,
            volumeRulesRegistryVersion: 0);

        walkableOnly.Should().NotBe(allowUnwalkable);
    }

    [Fact]
    public void Cache_ShouldNotAliasDistinctKeysWithEqualIntegerHashes()
    {
        PathRequestCacheKey worldA = CreateVolumeKey(
            CreateIndex(worldToken: 1, gridToken: 2, x: 0),
            CreateIndex(worldToken: 1, gridToken: 2, x: 1));
        PathRequestCacheKey worldB = CreateVolumeKey(
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

    private static PathRequestCacheKey CreateVolumeKey(
        WorldVoxelIndex origin,
        WorldVoxelIndex destination) =>
        PathRequestCacheKey.CreateVolume(
            origin,
            destination,
            Fixed64.One,
            allowUnwalkableEndpoints: false,
            HeuristicMethod.Manhattan,
            TraversalMedium.Gas,
            maxPathSearchRange: 32,
            volumeRulesRegistryVersion: 0);

    private static WorldVoxelIndex CreateIndex(long worldToken, long gridToken, int x) =>
        new(worldToken, gridIndex: 0, gridToken, new VoxelIndex(x, 0, 0));

}
