using System;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationCompositionWorkspaceTests
{
    [Fact]
    public void StringStampSet_Reset_ShouldHideEveryPriorGenerationValue()
    {
        var set = new NavigationStringStampSet(3);
        set.Add("A").Should().BeTrue();
        set.Add("B").Should().BeTrue();

        set.Reset();

        set.Contains("A").Should().BeFalse();
        set.Contains("B").Should().BeFalse();
        set.Add("A").Should().BeTrue();
    }

    [Fact]
    public void StringStampSet_ShouldUseOrdinalIdentityAndEnforceExactCapacity()
    {
        var set = new NavigationStringStampSet(2);

        set.Add("map").Should().BeTrue();
        set.Add("map").Should().BeFalse();
        set.Add("Map").Should().BeTrue();
        set.Contains("map").Should().BeTrue();
        set.Contains("Map").Should().BeTrue();

        Action overflow = () => set.Add("other");
        overflow.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void StringStampSet_RetainedBytes_ShouldMatchItsFixedAllocation()
    {
        var set = new NavigationStringStampSet(3);

        set.RetainedBytes.Should().Be(224);
        set.Add("A");
        set.Add("B");
        set.Reset();
        set.RetainedBytes.Should().Be(224);
    }

    [Fact]
    public void Workspace_Reset_ShouldReuseBuffersAndIsolateSetGenerations()
    {
        var workspace = new NavigationCompositionWorkspace(3);
        string[] domainQueue = workspace.DomainQueue;
        string[] buildQueue = workspace.BuildQueue;
        string[] rootKeys = workspace.RootKeys;
        workspace.Domain.Add("domain");
        workspace.RootKeySet.Add("root");
        workspace.BuildVisited.Add("visited");
        workspace.DomainQueue[0] = "stale";

        workspace.Reset();

        workspace.DomainQueue.Should().BeSameAs(domainQueue);
        workspace.BuildQueue.Should().BeSameAs(buildQueue);
        workspace.RootKeys.Should().BeSameAs(rootKeys);
        workspace.Domain.Contains("domain").Should().BeFalse();
        workspace.RootKeySet.Contains("root").Should().BeFalse();
        workspace.BuildVisited.Contains("visited").Should().BeFalse();
        workspace.DomainQueue[0].Should().Be("stale");
        workspace.RetainedBytes.Should().Be(888);
    }
}
