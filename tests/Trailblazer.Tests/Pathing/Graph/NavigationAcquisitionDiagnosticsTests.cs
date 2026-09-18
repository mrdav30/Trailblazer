using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationAcquisitionDiagnosticsTests
{
    [Fact]
    public void CompletedMeters_MustAccumulateIndependentCountersWithoutRetainingMutableMeter()
    {
        var diagnostics = new NavigationAcquisitionDiagnostics();
        var budget = new NavigationWorkBudget(100, 100, 100, 100, 100, 100, 100, 0, 100, 100, 100);
        var meter = new NavigationWorkMeter(budget);
        meter.TryConsumeLookupProbes(2).Should().BeTrue();
        meter.TryConsumeEndpointCandidates(3).Should().BeTrue();
        meter.TryConsumeExpandedNodes(5).Should().BeTrue();
        meter.TryConsumeEvaluatedEdges(7).Should().BeTrue();
        meter.TryConsumeConnectionLegs(11).Should().BeTrue();
        meter.TryConsumeTransitionCandidates(13).Should().BeTrue();
        meter.TryConsumeTransitionPairs(17).Should().BeTrue();
        meter.TryConsumeTraceIntervals(19).Should().BeTrue();
        meter.TryConsumeCoveredVoxelIntervals(23).Should().BeTrue();
        meter.TryConsumeSimplificationRays(29).Should().BeTrue();
        meter.RecordVolumeCandidate(true);
        meter.RecordVolumeCandidate(false);
        meter.RecordVolumeCandidate(false);
        meter.RecordVolumeUnionCheck();
        meter.RecordSuccessfulDependencyMerge();
        diagnostics.RecordCompletedQuery(meter);

        meter.Reset(budget);
        meter.TryConsumeLookupProbes(31).Should().BeTrue();
        meter.RecordVolumeCandidate(false);
        meter.RecordVolumeUnionCheck();
        meter.RecordVolumeUnionCheck();
        diagnostics.RecordCompletedQuery(meter);
        meter.Reset(budget);

        diagnostics.CompletedQueries.Should().Be(2);
        diagnostics.LookupProbes.Should().Be(33);
        diagnostics.EndpointCandidates.Should().Be(3);
        diagnostics.ExpandedNodes.Should().Be(5);
        diagnostics.EvaluatedEdges.Should().Be(7);
        diagnostics.ConnectionLegs.Should().Be(11);
        diagnostics.TransitionCandidates.Should().Be(13);
        diagnostics.TransitionPairs.Should().Be(17);
        diagnostics.TraceIntervals.Should().Be(19);
        diagnostics.CoveredVoxelIntervals.Should().Be(23);
        diagnostics.SimplificationRays.Should().Be(29);
        diagnostics.PrimaryVolumeCandidates.Should().Be(1);
        diagnostics.ShortcutVolumeCandidates.Should().Be(3);
        diagnostics.VolumeUnionChecks.Should().Be(3);
        diagnostics.SuccessfulDependencyMerges.Should().Be(1);
        diagnostics.BuildStarts.Should().Be(0, "meter aggregation cannot invent acquisition outcomes");
        diagnostics.CacheCheckoutHits.Should().Be(0);
        diagnostics.SharedFollowerCheckouts.Should().Be(0);
        diagnostics.SharedCoverageMisses.Should().Be(0);
        diagnostics.Promotions.Should().Be(0);
    }
}
