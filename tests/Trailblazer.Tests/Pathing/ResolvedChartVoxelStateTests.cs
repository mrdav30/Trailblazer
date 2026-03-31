using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

public class ResolvedChartVoxelStateTests
{
    [Fact]
    public void AddOwner_ShouldKeepCurrentWinner_WhenLowerPrecedenceOwnerChanges()
    {
        var state = new ResolvedChartVoxelState();

        state.AddOwner("Low", NavigationChartCell.Solid, priority: 0, registrationOrder: 0);
        state.AddOwner("High", NavigationChartCell.Gas, priority: 1, registrationOrder: 1);

        state.AddOwner("Low", NavigationChartCell.Liquid, priority: 0, registrationOrder: 0);

        Assert.True(state.ContainsOwner("Low"));
        Assert.True(state.ContainsOwner("High"));
        Assert.Equal("High", state.EffectiveChartOwner);
        Assert.Equal(NavigationChartCell.Gas, state.EffectiveCell);
    }

    [Fact]
    public void RemoveOwner_ShouldPromoteNextHighestPrecedenceOwner()
    {
        var state = new ResolvedChartVoxelState();

        state.AddOwner("First", NavigationChartCell.Solid, priority: 0, registrationOrder: 0);
        state.AddOwner("Second", NavigationChartCell.Gas, priority: 0, registrationOrder: 1);
        state.AddOwner("Third", NavigationChartCell.Liquid, priority: 2, registrationOrder: 2);

        state.RemoveOwner("Third");
        Assert.Equal("Second", state.EffectiveChartOwner);
        Assert.Equal(NavigationChartCell.Gas, state.EffectiveCell);

        state.RemoveOwner("Second");
        Assert.Equal("First", state.EffectiveChartOwner);
        Assert.Equal(NavigationChartCell.Solid, state.EffectiveCell);

        state.RemoveOwner("First");
        Assert.False(state.HasAnyOwners);
        Assert.Null(state.EffectiveChartOwner);
        Assert.Equal(NavigationChartCell.Empty, state.EffectiveCell);
    }
}
