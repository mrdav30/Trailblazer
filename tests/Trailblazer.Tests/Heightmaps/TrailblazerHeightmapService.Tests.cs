using System;
using FixedMathSharp;
using FluentAssertions;
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
    public void Register_ShouldRejectNullSurfacesAndInvalidSelectionRanges()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();

        Action nullSurface = () => context.Heightmaps.Register(null!, Fixed64.Zero, Fixed64.One);
        Action invalidRange = () => context.Heightmaps.Register(CreateSurface("Ground", height: 2), Fixed64.One, Fixed64.One);

        nullSurface.Should().Throw<ArgumentNullException>().WithParameterName("surface");
        invalidRange.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxSelectionY");
    }

    [Fact]
    public void Unregister_ShouldRemoveExistingLayerAndIgnoreInvalidNames()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        HeightmapSurface surface = CreateSurface("Ground", height: 2);
        context.Heightmaps.Register(surface, Fixed64.Zero, (Fixed64)4).Should().BeTrue();

        context.Heightmaps.Unregister(string.Empty).Should().BeFalse();
        context.Heightmaps.Unregister("Ground").Should().BeTrue();
        context.Heightmaps.Unregister("Ground").Should().BeFalse();
        context.Heightmaps.IsRegistered("Ground").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RegistryLookups_ShouldTreatInvalidLayerNamesAsMissing(string? layerName)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();

        context.Heightmaps.IsRegistered(layerName!).Should().BeFalse();
        context.Heightmaps.TryGetRegistration(layerName!, out HeightmapLayerRegistration registration)
            .Should().BeFalse();
        registration.Should().BeNull();
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

        context.Heightmaps.TryGetRegistration("Missing", out HeightmapLayerRegistration missing)
            .Should().BeFalse();
        missing.Should().BeNull();
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

    [Fact]
    public void TrySampleGround_ShouldSelectGroundLayer_WhenStackedLayersShareXZAndContactYIsGroundBand()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.Heightmaps.Register(CreateSurface("Ground", height: 0), -Fixed64.One, (Fixed64)2)
            .Should().BeTrue();
        context.Heightmaps.Register(CreateSurface("Platform", height: 3), (Fixed64)2, (Fixed64)4)
            .Should().BeTrue();

        context.Heightmaps.TrySampleGround(new Vector3d(Fixed64.Zero, Fixed64.Half, Fixed64.Zero), out HeightmapSample sample)
            .Should().BeTrue();

        sample.LayerName.Should().Be("Ground");
        sample.GroundY.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void TrySampleGround_ShouldSelectPlatformLayer_WhenStackedLayersShareXZAndContactYIsPlatformBand()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.Heightmaps.Register(CreateSurface("Ground", height: 0), -Fixed64.One, (Fixed64)2)
            .Should().BeTrue();
        context.Heightmaps.Register(CreateSurface("Platform", height: 3), (Fixed64)2, (Fixed64)4)
            .Should().BeTrue();

        context.Heightmaps.TrySampleGround(new Vector3d(Fixed64.Zero, (Fixed64)3, Fixed64.Zero), out HeightmapSample sample)
            .Should().BeTrue();

        sample.LayerName.Should().Be("Platform");
        sample.GroundY.Should().Be((Fixed64)3);
    }

    [Fact]
    public void TrySampleGround_WithPreferredLayer_ShouldKeepPreferredLayer_WhenItStillContainsQuery()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.Heightmaps.Register(CreateSurface("Ground", height: 0), Fixed64.Zero, (Fixed64)4)
            .Should().BeTrue();
        context.Heightmaps.Register(CreateSurface("Platform", height: 3), Fixed64.Zero, (Fixed64)4)
            .Should().BeTrue();

        context.Heightmaps.TrySampleGround(new Vector3d(Fixed64.Zero, (Fixed64)3, Fixed64.Zero), "Ground", out HeightmapSample sample)
            .Should().BeTrue();

        sample.LayerName.Should().Be("Ground");
        sample.GroundY.Should().Be(Fixed64.Zero);
        sample.DistanceFromSelectionY.Should().Be((Fixed64)3);
    }

    [Fact]
    public void TrySampleGround_WithPreferredLayer_ShouldFallBack_WhenPreferredLayerNoLongerContainsContactY()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.Heightmaps.Register(CreateSurface("Ground", height: 0), -Fixed64.One, (Fixed64)2)
            .Should().BeTrue();
        context.Heightmaps.Register(CreateSurface("Platform", height: 3), (Fixed64)2, (Fixed64)4)
            .Should().BeTrue();

        context.Heightmaps.TrySampleGround(new Vector3d(Fixed64.Zero, (Fixed64)3, Fixed64.Zero), "Ground", out HeightmapSample sample)
            .Should().BeTrue();

        sample.LayerName.Should().Be("Platform");
        sample.GroundY.Should().Be((Fixed64)3);
    }

    [Fact]
    public void TrySampleGround_ShouldUseHigherPriority_WhenCandidatesTieByDistance()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.Heightmaps.Register(CreateSurface("LowPriority", height: 0), Fixed64.Zero, (Fixed64)3)
            .Should().BeTrue();
        context.Heightmaps.Register(CreateSurface("HighPriority", height: 2), Fixed64.Zero, (Fixed64)3, priority: 10)
            .Should().BeTrue();

        context.Heightmaps.TrySampleGround(new Vector3d(Fixed64.Zero, Fixed64.One, Fixed64.Zero), out HeightmapSample sample)
            .Should().BeTrue();

        sample.LayerName.Should().Be("HighPriority");
        sample.GroundY.Should().Be((Fixed64)2);
    }

    [Fact]
    public void TrySampleGround_ShouldPreferCloserCandidate_WhenDistancesDiffer()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.Heightmaps.Register(CreateSurface("Far", height: 0), Fixed64.Zero, (Fixed64)4)
            .Should().BeTrue();
        context.Heightmaps.Register(CreateSurface("Near", height: 2), Fixed64.Zero, (Fixed64)4)
            .Should().BeTrue();

        context.Heightmaps.TrySampleGround(new Vector3d(Fixed64.Zero, (Fixed64)3, Fixed64.Zero), out HeightmapSample sample)
            .Should().BeTrue();

        sample.LayerName.Should().Be("Near");
        sample.GroundY.Should().Be((Fixed64)2);
    }

    [Fact]
    public void ServiceApis_ShouldThrowWhenOwningWorldIsInactive()
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        TrailblazerHeightmapService service = context.Heightmaps;
        context.World.Dispose();

        Action act = () => service.IsRegistered("Ground");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*inactive GridWorld*");
        context.Dispose();
    }

    [Fact]
    public void TrySampleGround_ShouldUseRegistrationOrder_WhenCandidatesTieByDistanceAndPriority()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.Heightmaps.Register(CreateSurface("First", height: 0), Fixed64.Zero, (Fixed64)2)
            .Should().BeTrue();
        context.Heightmaps.Register(CreateSurface("Second", height: 0), Fixed64.Zero, (Fixed64)2)
            .Should().BeTrue();

        context.Heightmaps.TrySampleGround(new Vector3d(Fixed64.Zero, Fixed64.Half, Fixed64.Zero), out HeightmapSample sample)
            .Should().BeTrue();

        sample.LayerName.Should().Be("First");
        sample.GroundY.Should().Be(Fixed64.Zero);
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
