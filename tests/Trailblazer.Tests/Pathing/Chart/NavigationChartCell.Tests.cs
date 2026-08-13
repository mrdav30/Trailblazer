using System;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

public class NavigationChartCellTests
{
    [Fact]
    public void NavigationChartCell_ShouldSupportAllIndividualMedia()
    {
        var solid = new NavigationChartCell(TraversalMedia.Solid);
        solid.HasSolid.Should().BeTrue();
        solid.HasVolume.Should().BeFalse();
        solid.HasTraversalData.Should().BeTrue();
        solid.CanGenerateTransition.Should().BeFalse();
        solid.SupportsMedium(TraversalMedium.Solid).Should().BeTrue();
        solid.SupportsMedium(TraversalMedium.Gas).Should().BeFalse();
        solid.SupportsMedium(TraversalMedium.Liquid).Should().BeFalse();
        solid.SupportsMedium(TraversalMedium.Unknown).Should().BeFalse();

        var gas = new NavigationChartCell(TraversalMedia.Gas);
        gas.HasSolid.Should().BeFalse();
        gas.HasVolume.Should().BeTrue();
        gas.SupportsMedium(TraversalMedium.Gas).Should().BeTrue();
        gas.SupportsMedium(TraversalMedium.Solid).Should().BeFalse();
        gas.SupportsMedium(TraversalMedium.Liquid).Should().BeFalse();

        var liquid = new NavigationChartCell(TraversalMedia.Liquid);
        liquid.HasSolid.Should().BeFalse();
        liquid.HasVolume.Should().BeTrue();
        liquid.SupportsMedium(TraversalMedium.Liquid).Should().BeTrue();
        liquid.SupportsMedium(TraversalMedium.Solid).Should().BeFalse();
        liquid.SupportsMedium(TraversalMedium.Gas).Should().BeFalse();
    }

    [Fact]
    public void NavigationChartCell_ShouldSupportCombinedSolidAndVolume()
    {
        var solidGas = new NavigationChartCell(TraversalMedia.Solid | TraversalMedia.Gas);
        solidGas.HasSolid.Should().BeTrue();
        solidGas.HasVolume.Should().BeTrue();
        solidGas.SupportsMedium(TraversalMedium.Solid).Should().BeTrue();
        solidGas.SupportsMedium(TraversalMedium.Gas).Should().BeTrue();
        solidGas.SupportsMedium(TraversalMedium.Liquid).Should().BeFalse();

        var solidLiquid = new NavigationChartCell(TraversalMedia.Solid | TraversalMedia.Liquid);
        solidLiquid.HasSolid.Should().BeTrue();
        solidLiquid.HasVolume.Should().BeTrue();
        solidLiquid.SupportsMedium(TraversalMedium.Solid).Should().BeTrue();
        solidLiquid.SupportsMedium(TraversalMedium.Liquid).Should().BeTrue();
        solidLiquid.SupportsMedium(TraversalMedium.Gas).Should().BeFalse();
    }

    [Fact]
    public void NavigationChartCell_StaticPresets_ShouldMatchExpectedMedia()
    {
        NavigationChartCell.Empty.HasTraversalData.Should().BeFalse();
        NavigationChartCell.Solid.SupportsMedium(TraversalMedium.Solid).Should().BeTrue();
        NavigationChartCell.Gas.SupportsMedium(TraversalMedium.Gas).Should().BeTrue();
        NavigationChartCell.SolidGas.SupportsMedium(TraversalMedium.Solid).Should().BeTrue();
        NavigationChartCell.SolidGas.SupportsMedium(TraversalMedium.Gas).Should().BeTrue();
        NavigationChartCell.Liquid.SupportsMedium(TraversalMedium.Liquid).Should().BeTrue();
        NavigationChartCell.SolidLiquid.SupportsMedium(TraversalMedium.Solid).Should().BeTrue();
        NavigationChartCell.SolidLiquid.SupportsMedium(TraversalMedium.Liquid).Should().BeTrue();
    }

    [Fact]
    public void NavigationChartCell_ShouldThrow_WhenGasAndLiquidCombined()
    {
        Action gasLiquid = () => _ = new NavigationChartCell(TraversalMedia.Gas | TraversalMedia.Liquid);
        gasLiquid.Should().Throw<ArgumentException>();

        Action solidGasLiquid = () => _ = new NavigationChartCell(TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid);
        solidGasLiquid.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NavigationChartCell_ShouldThrow_WhenGeneratedTransitionMediaIsNotSubset()
    {
        Action notSubset = () => _ = new NavigationChartCell(
            TraversalMedia.Solid,
            generatedTransitionMedia: TraversalMedia.Gas);
        notSubset.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NavigationChartCell_ShouldTrackGeneratedTransitionMedia()
    {
        var cell = new NavigationChartCell(
            TraversalMedia.Solid | TraversalMedia.Gas,
            generatedTransitionMedia: TraversalMedia.Gas);
        cell.CanGenerateTransition.Should().BeTrue();
        cell.GeneratedTransitionMedia.Should().Be(TraversalMedia.Gas);
    }

    [Fact]
    public void NavigationChartCell_ShouldCarryPathCostModifierAndFlags()
    {
        var cell = new NavigationChartCell(
            TraversalMedia.Solid,
            pathCostModifier: 7,
            flags: NavigationChartCellFlags.TransitionSourceHint);
        cell.PathCostModifier.Should().Be(7);
        cell.Flags.Should().Be(NavigationChartCellFlags.TransitionSourceHint);
    }
}
