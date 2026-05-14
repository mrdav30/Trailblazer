using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Heightmaps;
using Xunit;

namespace Trailblazer.Tests.Heightmaps;

public sealed class TrailblazerHeightmapServiceTests
{
    [Fact]
    public void Register_ShouldRejectDuplicateLayerNames_InsideOneContext()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        HeightmapSurface surface = CreateSurface("Ground", height: 2);

        context.Heightmaps.Register(surface, Fixed64.Zero, (Fixed64)4).Should().BeTrue();

        context.Heightmaps.Register(surface, Fixed64.Zero, (Fixed64)4).Should().BeFalse();
        context.Heightmaps.IsRegistered("Ground").Should().BeTrue();
    }

    [Fact]
    public void Register_ShouldKeepSameLayerNameIndependentAcrossContexts()
    {
        using TrailblazerWorldContext first = TrailblazerWorldContext.CreateOwned();
        using TrailblazerWorldContext second = TrailblazerWorldContext.CreateOwned();

        first.Heightmaps.Register(CreateSurface("Shared", height: 1), Fixed64.Zero, (Fixed64)2)
            .Should().BeTrue();
        second.Heightmaps.Register(CreateSurface("Shared", height: 3), (Fixed64)2, (Fixed64)4)
            .Should().BeTrue();

        first.Heightmaps.TrySampleGround(Vector3d.Zero, out HeightmapSample firstSample).Should().BeTrue();
        second.Heightmaps.TrySampleGround(new Vector3d(Fixed64.Zero, (Fixed64)3, Fixed64.Zero), out HeightmapSample secondSample)
            .Should().BeTrue();
        firstSample.GroundY.Should().Be((Fixed64)1);
        secondSample.GroundY.Should().Be((Fixed64)3);
    }

    [Fact]
    public void Reset_ShouldClearOnlyCurrentContextHeightmapRegistry()
    {
        using TrailblazerWorldContext first = TrailblazerWorldContext.CreateOwned();
        using TrailblazerWorldContext second = TrailblazerWorldContext.CreateOwned();

        first.Heightmaps.Register(CreateSurface("First", height: 1), Fixed64.Zero, (Fixed64)2)
            .Should().BeTrue();
        second.Heightmaps.Register(CreateSurface("Second", height: 2), Fixed64.Zero, (Fixed64)3)
            .Should().BeTrue();

        first.Reset();

        first.Heightmaps.IsRegistered("First").Should().BeFalse();
        second.Heightmaps.IsRegistered("Second").Should().BeTrue();
    }

    [Fact]
    public void ServiceApis_ShouldThrowClearDisposedContextError_AfterContextDisposal()
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        TrailblazerHeightmapService service = context.Heightmaps;
        context.Dispose();

        Action act = () => service.IsRegistered("Ground");

        act.Should().Throw<ObjectDisposedException>()
            .WithMessage("*TrailblazerWorldContext*");
    }

    [Fact]
    public void TryGetRegistration_ShouldExposeRegisteredLayerMetadata()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        HeightmapSurface surface = CreateSurface("Ground", height: 2);

        context.Heightmaps.Register(surface, Fixed64.Zero, (Fixed64)4, priority: 7).Should().BeTrue();

        context.Heightmaps.TryGetRegistration("Ground", out HeightmapLayerRegistration registration)
            .Should().BeTrue();
        registration.LayerName.Should().Be("Ground");
        registration.Surface.Should().BeSameAs(surface);
        registration.MinSelectionY.Should().Be(Fixed64.Zero);
        registration.MaxSelectionY.Should().Be((Fixed64)4);
        registration.Priority.Should().Be(7);
        registration.RegistrationOrder.Should().Be(0);
    }

    [Fact]
    public void TrySampleGround_ShouldReturnSampleForRegisteredLayer()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.Heightmaps.Register(CreateSurface("Ground", height: 6), (Fixed64)4, (Fixed64)8)
            .Should().BeTrue();

        Vector3d query = new(Fixed64.Zero, (Fixed64)5, Fixed64.Zero);

        context.Heightmaps.TrySampleGround(query, out HeightmapSample sample).Should().BeTrue();
        sample.LayerName.Should().Be("Ground");
        sample.QueryPosition.Should().Be(query);
        sample.GroundY.Should().Be((Fixed64)6);
        sample.DistanceFromSelectionY.Should().Be(Fixed64.One);
    }

    private static HeightmapSurface CreateSurface(string name, int height)
    {
        return HeightmapSurface.FromHeights(
            name,
            new Fixed64[1, 1] { { (Fixed64)height } },
            Vector3d.Zero,
            Fixed64.One,
            new HeightmapCompression(Fixed64.Zero, Fixed64.One));
    }
}
