using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Trailblazer.Tests.Architecture;

public sealed class RuntimeArchitectureGuardTests
{
    private static readonly string[] AmbientWorldBridgeMarkers =
    {
        "TrailblazerWorldManager",
        "DefaultContext",
        "HasDefaultContext",
        "compatibility facade",
        "default-context",
        "FallbackState"
    };

    private static readonly string[] EngineSpecificMarkers =
    {
        "UnityEngine",
        "Godot",
        "UnrealEngine",
        "Microsoft.Xna.Framework",
        "MonoGame",
        "Stride.Engine"
    };

    private static readonly string[] WallClockMarkers =
    {
        "DateTime.Now",
        "DateTime.UtcNow",
        "Stopwatch",
        "System.Threading.Timer",
        "Task.Delay",
        "Thread.Sleep",
        "Environment.TickCount"
    };

    [Fact]
    public void ProductionCode_ShouldNotContainAmbientWorldBridgeReferences()
    {
        string sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "Trailblazer");
        var violations = FindProductionMarkerViolations(sourceRoot, AmbientWorldBridgeMarkers);

        violations.Should().BeEmpty(
            "Trailblazer runtime code should only use explicit world contexts");
    }

    [Fact]
    public void ProductionSimulationCode_ShouldRemainEngineAgnosticAndFrameDriven()
    {
        string sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "Trailblazer");
        string[] forbiddenMarkers = EngineSpecificMarkers.Concat(WallClockMarkers).ToArray();
        var violations = FindProductionMarkerViolations(sourceRoot, forbiddenMarkers);

        violations.Should().BeEmpty(
            "Trailblazer runtime code must stay deterministic, engine-agnostic, and driven by fixed simulation frames");
    }

    private static List<string> FindProductionMarkerViolations(string sourceRoot, string[] markers)
    {
        var result = new List<string>();
        foreach (string file in EnumerateProductionSourceFiles(sourceRoot))
        {
            string text = File.ReadAllText(file);
            foreach (string marker in markers)
            {
                if (text.IndexOf(marker, StringComparison.Ordinal) >= 0)
                    result.Add($"{Path.GetRelativePath(sourceRoot, file)} contains '{marker}'");
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static IEnumerable<string> EnumerateProductionSourceFiles(string sourceRoot)
    {
        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Trailblazer"))
                && Directory.Exists(Path.Combine(directory.FullName, "tests", "Trailblazer.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate the Trailblazer repository root.");
    }
}
