using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Trailblazer.Tests.Worlds;

public sealed class MultiWorldArchitectureGuardTests
{
    private const int Phase0TrailblazerWorldManagerReferenceFileBaseline = 28;

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
    public void ProductionTrailblazerWorldManagerReferences_ShouldNotIncreaseBeyondPhase0Baseline()
    {
        string sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "Trailblazer");
        List<string> filesWithReferences = FindProductionFilesContaining(sourceRoot, "TrailblazerWorldManager");

        filesWithReferences.Count.Should().BeLessThanOrEqualTo(
            Phase0TrailblazerWorldManagerReferenceFileBaseline,
            "the multi-world migration should monotonically remove ambient world bridge references");
    }

    [Fact]
    public void ProductionSimulationCode_ShouldRemainEngineAgnosticAndFrameDriven()
    {
        string sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "Trailblazer");
        string[] forbiddenMarkers = EngineSpecificMarkers.Concat(WallClockMarkers).ToArray();
        var violations = new List<string>();

        foreach (string file in EnumerateProductionSourceFiles(sourceRoot))
        {
            string text = File.ReadAllText(file);
            foreach (string marker in forbiddenMarkers)
            {
                if (text.IndexOf(marker, StringComparison.Ordinal) >= 0)
                    violations.Add($"{Path.GetRelativePath(sourceRoot, file)} contains '{marker}'");
            }
        }

        violations.Should().BeEmpty(
            "Trailblazer runtime code must stay deterministic, engine-agnostic, and driven by fixed simulation frames");
    }

    private static List<string> FindProductionFilesContaining(string sourceRoot, string marker)
    {
        var result = new List<string>();
        foreach (string file in EnumerateProductionSourceFiles(sourceRoot))
        {
            string text = File.ReadAllText(file);
            if (text.IndexOf(marker, StringComparison.Ordinal) >= 0)
                result.Add(Path.GetRelativePath(sourceRoot, file));
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
