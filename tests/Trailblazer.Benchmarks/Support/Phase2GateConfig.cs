//=======================================================================
// Phase2GateConfig.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Mathematics;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Trailblazer.Benchmarks;

/// <summary>Runs repeated Phase 2 gate samples out of process against the local LSF stack.</summary>
internal sealed class Phase2GateConfig : ManualConfig
{
    public Phase2GateConfig()
    {
        AddJob(Job.Default
            .WithStrategy(RunStrategy.Monitoring)
            .WithLaunchCount(3)
            .WithWarmupCount(10)
            .WithIterationCount(100)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .WithMsBuildArguments(
                "/p:UseLocalLsfStack=true",
                "/p:UsePrebuiltLocalLsfStack=true",
                "/m:1"));
        AddColumn(StatisticColumn.P95, Phase2P99Column.Instance);
        AddExporter(MarkdownExporter.Default, JsonExporter.FullCompressed);
    }

    private sealed class Phase2P99Column : IColumn
    {
        internal static readonly Phase2P99Column Instance = new();

        public string Id => "P99";
        public string ColumnName => "P99 (ns)";
        public bool AlwaysShow => true;
        public ColumnCategory Category => ColumnCategory.Statistics;
        public int PriorityInCategory => 99;
        public bool IsNumeric => true;
        public UnitType UnitType => UnitType.Dimensionless;
        public string Legend => "99th percentile in nanoseconds.";

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
            GetValue(summary, benchmarkCase, summary.Style);

        public string GetValue(
            Summary summary,
            BenchmarkCase benchmarkCase,
            SummaryStyle style)
        {
            Statistics statistics = summary[benchmarkCase].ResultStatistics;
            if (statistics == null)
                return "NA";
            double value = statistics.Percentiles.Percentile(99);
            return value.ToString("0.###", style.CultureInfo);
        }

        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

        public bool IsAvailable(Summary summary) => true;
    }
}
