//=======================================================================
// NavigationColdGuideBenchmarks.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using BenchmarkDotNet.Attributes;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures matched public cold, internal cold, and public warm acquisition boundaries.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(PerformanceGateConfig))]
[BenchmarkCategory("Graph", "Flow", "Cold", "Public")]
public class NavigationColdGuideBenchmarks
{
    private ColdGuideBenchmarkScenario _scenario;

    [Params("Plane100", "Plane10000", "Plane100000", "Ladder100")]
    public string Scenario { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        bool ladder = Scenario.StartsWith("Ladder", System.StringComparison.Ordinal);
        int nodes = int.Parse(Scenario.Substring(ladder ? 6 : 5), System.Globalization.CultureInfo.InvariantCulture);
        _scenario = new ColdGuideBenchmarkScenario(ladder ? "Ladder" : "Plane", nodes);
    }

    [IterationSetup(Targets = new[] { nameof(ColdPublicFlow), nameof(ColdPublicAStar), nameof(ColdInternalFlow) })]
    public void PrepareCold() => _scenario.ClearCaches();

    [IterationSetup(Target = nameof(WarmPublicFlow))]
    public void PrepareWarm()
    {
        _scenario.RequestFlow();
        _scenario.ValidateFlowPayload(_scenario.FlowQuery);
    }

    [IterationCleanup(Targets = new[] { nameof(ColdPublicFlow), nameof(WarmPublicFlow) })]
    public void ValidateFlow()
    {
        _scenario.ValidateFlowPayload(_scenario.FlowQuery);
        _scenario.ValidateDrained();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scenario.WriteCounters();
        _scenario.Dispose();
    }

    [Benchmark]
    public long ColdPublicFlow() => _scenario.RequestFlow();

    [Benchmark]
    public long ColdPublicAStar() => _scenario.RequestAStar();

    [Benchmark]
    public long ColdInternalFlow() => _scenario.InternalFlow();

    [Benchmark]
    public long WarmPublicFlow() => _scenario.RequestFlow();
}
