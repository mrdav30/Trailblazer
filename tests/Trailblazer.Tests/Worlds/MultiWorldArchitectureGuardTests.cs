using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Trailblazer.Tests.Worlds;

public sealed class MultiWorldArchitectureGuardTests
{
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
        var forbiddenMarkers = new[]
        {
            "TrailblazerWorldManager",
            "DefaultContext",
            "HasDefaultContext",
            "compatibility facade",
            "default-context",
            "FallbackState"
        };
        var violations = FindProductionMarkerViolations(sourceRoot, forbiddenMarkers);

        violations.Should().BeEmpty(
            "Trailblazer is pre-alpha and the public/runtime API should only expose explicit world contexts");
    }

    [Fact]
    public void Tests_ShouldNotKeepPhase0CompatibilitySuites()
    {
        string testRoot = Path.Combine(FindRepositoryRoot(), "tests", "Trailblazer.Tests");
        File.Exists(Path.Combine(testRoot, "Worlds", "MultiWorldPhase0AcceptanceTests.cs")).Should().BeFalse(
            "Phase 0 acceptance coverage should be merged into the owning context/pathing/navigation suites");
    }

    [Fact]
    public void PublicDocs_ShouldDescribeContextOnlyApiWithoutMigrationLanguage()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] docs =
        {
            Path.Combine(repositoryRoot, "README.md"),
            Path.Combine(repositoryRoot, "docs", "wiki", "OVERVIEW.md"),
            Path.Combine(repositoryRoot, "docs", "wiki", "PATHING.MD"),
            Path.Combine(repositoryRoot, "docs", "wiki", "PATHMANAGER.MD"),
            Path.Combine(repositoryRoot, "docs", "wiki", "PATHGUIDES.MD"),
            Path.Combine(repositoryRoot, "docs", "wiki", "TRANSITIONS.MD"),
            Path.Combine(repositoryRoot, "docs", "wiki", "VOLUMETRAVERSAL.MD"),
            Path.Combine(repositoryRoot, "docs", "wiki", "NAVIGATOR.MD"),
            Path.Combine(repositoryRoot, "docs", "wiki", "NAVSTEERING.MD"),
            Path.Combine(repositoryRoot, "docs", "wiki", "SERIALIZATION.MD")
        };

        var forbiddenMarkers = new[]
        {
            "DefaultContext",
            "TrailblazerWorldManager",
            "compatibility facade",
            "default-context",
            "legacy static",
            "now has",
            "now owns",
            "now uses",
            "remains available"
        };
        var violations = new List<string>();

        foreach (string doc in docs)
        {
            string text = File.ReadAllText(doc);
            foreach (string marker in forbiddenMarkers)
            {
                if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    violations.Add($"{Path.GetRelativePath(repositoryRoot, doc)} contains '{marker}'");
            }
        }

        violations.Should().BeEmpty(
            "public docs should present the context-only API as the current API, not as a migration bridge");
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
