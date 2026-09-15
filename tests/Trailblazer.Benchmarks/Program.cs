using System;
using System.Linq;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Trailblazer.Benchmarks.Pathing;

namespace Trailblazer.Benchmarks;

internal static class Program
{
    private static readonly BenchmarkCatalog _catalog = BenchmarkCatalog.Create(typeof(Program).Assembly);

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return BenchmarkExitCode.Get(BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args));
        }

        string command = args[0];

        if (string.Equals(command, "direct-visibility-smoke", StringComparison.OrdinalIgnoreCase)
            && args.Length == 1)
            return NavigationDirectVisibilityFrameBenchmarks.RunSmoke();

        if (string.Equals(command, "direct-visibility-smoke", StringComparison.OrdinalIgnoreCase)
            && args.Length == 2 && args[1] == "--list")
        {
            // BDN's flat list omits parameter values; retain its actual expanded cases for audit.
            var run = BenchmarkConverter.TypeToBenchmarks(
                typeof(NavigationDirectVisibilityFrameBenchmarks), CreateGlobalConfig(false));
            foreach (var benchmark in run.BenchmarksCases)
                Console.WriteLine(benchmark.DisplayInfo);
            return 0;
        }

        if (string.Equals(command, "guided-phase-smoke", StringComparison.OrdinalIgnoreCase)
            && args.Length == 1)
        {
            return NavigationGuidedFrameBenchmarks.RunPhaseSpreadingSmoke();
        }

        if ((string.Equals(command, "list", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(command, "ls", StringComparison.OrdinalIgnoreCase)) &&
            args.Length == 1)
        {
            _catalog.WriteAvailableSelections(Console.Out);
            return 0;
        }

        if (string.Equals(command, "help", StringComparison.OrdinalIgnoreCase))
        {
            WriteUsage();
            return 0;
        }

        if (string.Equals(command, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 1 && !args[1].StartsWith("-", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("The 'all' selection cannot be combined with other benchmark aliases.");
                Console.Error.WriteLine();
                WriteUsage();
                return 1;
            }

            return BenchmarkExitCode.Get(BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args.Skip(1).ToArray()));
        }

        int aliasCount = 0;
        while (aliasCount < args.Length && !args[aliasCount].StartsWith("-", StringComparison.Ordinal))
            aliasCount++;

        if (aliasCount == 0)
        {
            return BenchmarkExitCode.Get(BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args));
        }

        Type[] selectedTypes = _catalog.Resolve(args.Take(aliasCount).ToArray(), out string unknownAlias);
        if (unknownAlias != null)
        {
            Console.Error.WriteLine($"Unknown benchmark selection '{unknownAlias}'.");
            Console.Error.WriteLine();
            WriteUsage();
            return 1;
        }

        string[] benchmarkArgs = args.Skip(aliasCount).ToArray();
        if (!benchmarkArgs.Any(argument =>
                string.Equals(argument, "--filter", StringComparison.OrdinalIgnoreCase)))
        {
            benchmarkArgs = benchmarkArgs.Concat(new[] { "--filter", "*" }).ToArray();
        }

        bool hasCommandLineJob = benchmarkArgs.Any(argument =>
            string.Equals(argument, "--job", StringComparison.OrdinalIgnoreCase));
        if (hasCommandLineJob)
        {
            return BenchmarkExitCode.Get(BenchmarkSwitcher.FromTypes(selectedTypes).Run(
                benchmarkArgs,
                DefaultConfig.Instance));
        }

        Type[] singleShotTypes = selectedTypes
            .Where(type => string.Equals(
                type.Name,
                "NavigationFlowFieldArticulationBenchmarks",
                StringComparison.Ordinal))
            .ToArray();
        Type[] canonicalTypes = selectedTypes.Except(singleShotTypes).ToArray();
        int exitCode = 0;
        if (canonicalTypes.Length > 0)
            exitCode |= BenchmarkExitCode.Get(BenchmarkSwitcher.FromTypes(canonicalTypes).Run(benchmarkArgs, CreateGlobalConfig(false)));
        if (singleShotTypes.Length > 0)
            exitCode |= BenchmarkExitCode.Get(BenchmarkSwitcher.FromTypes(singleShotTypes).Run(benchmarkArgs, CreateGlobalConfig(true)));
        return exitCode;
    }

    private static IConfig CreateGlobalConfig(bool singleShot) =>
        ManualConfig.Union(
                DefaultConfig.Instance,
                new PerformanceGateConfig(ConfigUnionRule.Union, singleShot))
            .WithUnionRule(ConfigUnionRule.AlwaysUseGlobal);

    private static void WriteUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0");
        Console.WriteLine("  dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- all --list flat");
        Console.WriteLine();
        Console.WriteLine("Leading arguments that do not start with '-' are treated as benchmark selections.");
        Console.WriteLine("Remaining arguments are forwarded to BenchmarkDotNet.");
        Console.WriteLine();
        _catalog.WriteAvailableSelections(Console.Out);
    }
}
