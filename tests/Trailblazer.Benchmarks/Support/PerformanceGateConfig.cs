//=======================================================================
// PerformanceGateConfig.cs
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

/// <summary>Runs repeated performance-gate samples out of process against the local LSF stack.</summary>
internal sealed class PerformanceGateConfig : ManualConfig
{
    public PerformanceGateConfig()
        : this(ConfigUnionRule.AlwaysUseGlobal, singleShot: false)
    {
    }

    internal PerformanceGateConfig(ConfigUnionRule unionRule, bool singleShot)
    {
        UnionRule = unionRule;
        Job job = singleShot ? Job.Dry.WithId("SingleShot") : Job.Default;
        AddJob(job
            .WithStrategy(RunStrategy.Monitoring)
            .WithLaunchCount(singleShot ? 1 : 3)
            .WithWarmupCount(singleShot ? 0 : 10)
            .WithIterationCount(singleShot ? 1 : 100)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .WithMsBuildArguments(
                "/p:UseLocalLsfStack=true",
                "/p:UsePrebuiltLocalLsfStack=true",
                "/m:1"));
        AddColumn(StatisticColumn.P95, P99Column.Instance);
        AddExporter(MarkdownExporter.Default, JsonExporter.FullCompressed);
    }

    private sealed class P99Column : IColumn
    {
        internal static readonly P99Column Instance = new();

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
