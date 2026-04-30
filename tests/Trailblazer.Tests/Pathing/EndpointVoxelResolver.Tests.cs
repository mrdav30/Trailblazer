using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class EndpointVoxelResolverTests : IDisposable
{
    public EndpointVoxelResolverTests()
    {
        if (TrailblazerWorldManager.IsActive)
            TrailblazerWorldManager.Reset();
        else
            TrailblazerWorldManager.Setup();

        TrailblazerWorldManager.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)), out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryGetEndpointVoxel_ShouldReturnFalse_WhenPolicyCannotResolve()
    {
        bool resolved = EndpointVoxelResolver.TryGetEndpointVoxel(
            Vector3d.Zero,
            new Vector3d(1, 1, 1),
            out Voxel? voxel,
            allowUnwalkableEndpoints: true,
            unitSize: Fixed64.One,
            new TestEndpointPolicy(canResolve: false));

        resolved.Should().BeFalse();
        voxel.Should().BeNull();
    }

    [Fact]
    public void TryGetEndpointVoxel_ShouldTraceFromDirectVoxel_WhenRelaxedEndpointNeedsFallback()
    {
        bool resolved = EndpointVoxelResolver.TryGetEndpointVoxel(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            out Voxel? voxel,
            allowUnwalkableEndpoints: true,
            unitSize: Fixed64.One,
            new TestEndpointPolicy(
                isTraversable: candidate => candidate.WorldPosition == new Vector3d(1, 0, 0)));

        resolved.Should().BeTrue();
        TestRequire.NotNull(voxel).WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void TryGetEndpointVoxel_ShouldUseFinalFallback_WhenTraceFailsAfterDirectVoxelLookup()
    {
        Voxel fallbackVoxel = GetVoxel(new Vector3d(2, 0, 0));

        bool resolved = EndpointVoxelResolver.TryGetEndpointVoxel(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            out Voxel? voxel,
            allowUnwalkableEndpoints: true,
            unitSize: Fixed64.One,
            new TestEndpointPolicy(
                finalFallbackVoxel: fallbackVoxel,
                finalFallbackSuccess: true));

        resolved.Should().BeTrue();
        voxel.Should().BeSameAs(fallbackVoxel);
    }

    [Fact]
    public void TryGetEndpointVoxel_ShouldTraceTowardGrid_WhenDirectVoxelIsMissing()
    {
        bool resolved = EndpointVoxelResolver.TryGetEndpointVoxel(
            new Vector3d(-8, 0, 0),
            Vector3d.Zero,
            out Voxel? voxel,
            allowUnwalkableEndpoints: true,
            unitSize: Fixed64.One,
            new TestEndpointPolicy(isTraversable: candidate => candidate.WorldPosition == Vector3d.Zero));

        resolved.Should().BeTrue();
        TestRequire.NotNull(voxel).WorldPosition.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void TryGetEndpointVoxel_ShouldReturnFalse_WhenDirectVoxelIsMissing_AndTraceCannotFindFallback()
    {
        bool resolved = EndpointVoxelResolver.TryGetEndpointVoxel(
            new Vector3d(-8, 0, 0),
            Vector3d.Zero,
            out Voxel? voxel,
            allowUnwalkableEndpoints: true,
            unitSize: Fixed64.One,
            new TestEndpointPolicy());

        resolved.Should().BeFalse();
        voxel.Should().BeNull();
    }

    [Fact]
    public void TryGetClosestTraversableVoxel_ShouldSearchDiagonalNeighbors_WhenPerpendicularNeighborsFail()
    {
        Voxel origin = GetVoxel(Vector3d.Zero);

        bool resolved = EndpointVoxelResolver.TryGetClosestTraversableVoxel(
            origin,
            out Voxel? voxel,
            Fixed64.One,
            new TestEndpointPolicy(isTraversable: candidate => candidate.WorldPosition == new Vector3d(1, 0, 1)));

        resolved.Should().BeTrue();
        TestRequire.NotNull(voxel).WorldPosition.Should().Be(new Vector3d(1, 0, 1));
    }

    [Fact]
    public void TryGetClosestTraversableVoxel_ShouldClearOutVoxel_WhenNoNeighborIsTraversable()
    {
        Voxel origin = GetVoxel(Vector3d.Zero);

        bool resolved = EndpointVoxelResolver.TryGetClosestTraversableVoxel(
            origin,
            out Voxel? voxel,
            Fixed64.One,
            new TestEndpointPolicy());

        resolved.Should().BeFalse();
        voxel.Should().BeNull();
    }

    private static Voxel GetVoxel(Vector3d position)
    {
        Voxel voxel = TestRequire.VoxelAt(position);
        return voxel;
    }

    private readonly struct TestEndpointPolicy : IVoxelEndpointResolutionPolicy
    {
        private readonly bool _canResolve;
        private readonly Func<Voxel, bool>? _isTraversable;
        private readonly Voxel? _finalFallbackVoxel;
        private readonly bool _finalFallbackSuccess;

        public TestEndpointPolicy(
            bool canResolve = true,
            Func<Voxel, bool>? isTraversable = null,
            Voxel? finalFallbackVoxel = null,
            bool finalFallbackSuccess = false)
        {
            _canResolve = canResolve;
            _isTraversable = isTraversable;
            _finalFallbackVoxel = finalFallbackVoxel;
            _finalFallbackSuccess = finalFallbackSuccess;
        }

        public bool CanResolve() => _canResolve;

        public bool TryAcceptDirectVoxel(Voxel voxel, Fixed64 unitSize, bool allowUnwalkableEndpoints) => false;

        public bool RequiresSizeFallback(Voxel voxel, Fixed64 unitSize) => false;

        public bool IsTraversable(Voxel voxel, Fixed64 unitSize) => _isTraversable?.Invoke(voxel) == true;

        public bool TryGetFinalFallbackVoxel(
            Vector3d position,
            Voxel directVoxel,
            Fixed64 unitSize,
            out Voxel voxel)
        {
            voxel = _finalFallbackVoxel!;
            return _finalFallbackSuccess;
        }
    }
}
