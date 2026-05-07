using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Benchmarks.Navigation;
using Trailblazer.Benchmarks.Pathing;
using Xunit;

namespace Trailblazer.Tests.Benchmarks;

[Collection("PathingCollection")]
public sealed class BenchmarkHarnessPreflightTests
{
    [Fact]
    public void AStarPathRequestBenchmarks_ShouldKeepAllConfiguredRequestsValid_AfterGlobalSetup()
    {
        var benchmarks = new AStarPathRequestBenchmarks();

        try
        {
            benchmarks.GlobalSetup();

            benchmarks.RawSurvey_OpenPlane32().HasPath.Should().BeTrue();
            benchmarks.ColdGuide_OpenPlane32().Should().BeTrue();
            benchmarks.ColdGuide_Corridor64().Should().BeTrue();
            benchmarks.ColdGuide_Corridor256().Should().BeTrue();
            benchmarks.ColdGuide_Corridor1024().Should().BeTrue();
            benchmarks.ColdGuide_BlockerField64().Should().BeTrue();
            benchmarks.ColdGuide_Heuristic_Manhattan().Should().BeTrue();
            benchmarks.ColdGuide_Heuristic_Octile().Should().BeTrue();
            benchmarks.ColdGuide_Heuristic_Euclidean().Should().BeTrue();
            benchmarks.FailedRoute_ChokeUnitSize2().Should().BeFalse();
        }
        finally
        {
            benchmarks.GlobalCleanup();
        }
    }

    [Fact]
    public void VolumePathRequestBenchmarks_ShouldKeepAllConfiguredRequestsValid_AfterGlobalSetup()
    {
        var benchmarks = new VolumePathRequestBenchmarks();

        try
        {
            benchmarks.GlobalSetup();

            benchmarks.RawSurvey_DirectGasCorridor().Should().BeTrue();
            benchmarks.ColdGuide_DirectGasCorridor().Should().BeTrue();
            benchmarks.ColdGuide_LShapeGasPath().Should().BeTrue();
            benchmarks.WarmGuide_DirectGasCorridor().Should().BeTrue();
            benchmarks.WarmGuide_LShapeGasPath().Should().BeTrue();
        }
        finally
        {
            benchmarks.GlobalCleanup();
        }
    }

    [Fact]
    public void NavSteeringBenchmarks_ShouldKeepGuidedScenariosValid_AfterGlobalSetup()
    {
        var benchmarks = new NavSteeringBenchmarks();

        try
        {
            benchmarks.GlobalSetup();

            benchmarks.FirstFrame_DirectLOS().Should().NotBe(default);
            benchmarks.FirstFrame_GuidedAStar().Should().NotBe(default);
            benchmarks.SteadyState_GuidedAStar().Should().NotBe(default);
            benchmarks.SteadyState_GuidedFlowField().Should().NotBe(default);
        }
        finally
        {
            benchmarks.GlobalCleanup();
        }
    }
}
