using System;
using FluentAssertions;
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

    /// <summary>
    /// Covers the early-return branch in <c>RemoveOwner</c> when the chart key is not present.
    /// </summary>
    [Fact]
    public void RemoveOwner_ShouldDoNothing_WhenChartIsNotPresent()
    {
        var state = new ResolvedChartVoxelState();
        state.AddOwner("Existing", NavigationChartCell.Solid, priority: 0, registrationOrder: 0);

        // Removing a key that was never added must not throw and must leave state intact.
        state.RemoveOwner("NotPresent");

        state.HasAnyOwners.Should().BeTrue();
        state.EffectiveChartOwner.Should().Be("Existing");
    }

    /// <summary>
    /// Covers the early-return branch in <c>AddChartOwnersTo</c> when the destination is null.
    /// </summary>
    [Fact]
    public void AddChartOwnersTo_ShouldDoNothing_WhenDestinationIsNull()
    {
        var state = new ResolvedChartVoxelState();
        state.AddOwner("Chart", NavigationChartCell.Solid, priority: 0, registrationOrder: 0);

        // Passing null must not throw.
        Action act = () => state.AddChartOwnersTo(null!);
        act.Should().NotThrow();
    }

    /// <summary>
    /// Covers the <c>string.CompareOrdinal</c> tie-break in <c>HasHigherPrecedence</c> when both
    /// priority and registrationOrder are identical across two owners.
    /// </summary>
    [Fact]
    public void HasHigherPrecedence_ShouldUseStringComparison_WhenPriorityAndOrderAreTied()
    {
        var state = new ResolvedChartVoxelState();

        // Same priority, same registrationOrder — string comparison decides the winner.
        // "Bravo" > "Alpha" lexicographically, so Bravo should win.
        state.AddOwner("Alpha", NavigationChartCell.Solid, priority: 0, registrationOrder: 0);
        state.AddOwner("Bravo", NavigationChartCell.Gas, priority: 0, registrationOrder: 0);

        state.EffectiveChartOwner.Should().Be("Bravo",
            "the lexicographically later chart name wins when priority and order are tied");
        state.EffectiveCell.Should().Be(NavigationChartCell.Gas);
    }
}
