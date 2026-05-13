using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Trailblazer.Pathing;
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

    private static readonly string[] ActivePathingStateFacadeFiles =
    {
        NormalizeRelativeSourcePath("Pathing/PathManager.cs"),
        NormalizeRelativeSourcePath("Pathing/VolumeRules/VolumeMediumRules.cs"),
        NormalizeRelativeSourcePath("Pathing/Transition/Registry/TraversalTransitionRegistry.cs")
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

    [Fact]
    public void ProductionCode_ShouldRestrictActivePathingStateLookupsToStaticFacades()
    {
        string sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "Trailblazer");
        var allowedFiles = new HashSet<string>(ActivePathingStateFacadeFiles, StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (string file in EnumerateProductionSourceFiles(sourceRoot))
        {
            string relativePath = Path.GetRelativePath(sourceRoot, file);
            string normalizedPath = NormalizeRelativeSourcePath(relativePath);
            if (allowedFiles.Contains(normalizedPath))
                continue;

            string text = File.ReadAllText(file);
            if (text.IndexOf("PathManager.TryGetActiveState(", StringComparison.Ordinal) >= 0)
                violations.Add($"{normalizedPath} calls PathManager.TryGetActiveState");
        }

        violations.Sort(StringComparer.Ordinal);
        violations.Should().BeEmpty(
            "new runtime code should carry explicit TrailblazerWorldContext ownership instead of inferring ambient pathing state");
    }

    [Fact]
    public void AlternativeVoxelFinder_ShouldNotExposeSharedAmbientInstance()
    {
        typeof(AlternativeVoxelFinder)
            .GetProperty("Shared", BindingFlags.Public | BindingFlags.Static)
            .Should()
            .BeNull("voxel fallback search state must be owned by a TrailblazerWorldContext");
    }

    [Fact]
    public void SurveyResultFactories_ShouldRequireExplicitContext()
    {
        Type[] resultTypes =
        {
            typeof(AStarSurveyResult),
            typeof(FlowFieldSurveyResult),
            typeof(VolumeSurveyResult),
            typeof(HybridRoutePlanSurveyResult)
        };

        var violations = FindPublicStaticMethodsWithoutFirstContextParameter(resultTypes, "Create");

        violations.Should().BeEmpty(
            "survey results must not infer ownership from PathManager.ActiveState");
    }

    [Fact]
    public void FlowFieldStaticHelpers_ShouldRequireExplicitContext()
    {
        string[] helperNames =
        {
            nameof(FlowFieldSurveyor.SampleFlowVector),
            nameof(FlowFieldSurveyor.TryGetNearestFlowAnchor),
            nameof(FlowFieldSurveyor.GetFlowDirection),
            nameof(FlowFieldSurveyor.GetFlowField)
        };

        var violations = FindPublicStaticMethodsWithoutFirstContextParameter(
            new[] { typeof(FlowFieldSurveyor) },
            helperNames);

        violations.Should().BeEmpty(
            "static flow-field helpers must sample against one explicit world context");
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

    private static List<string> FindPublicStaticMethodsWithoutFirstContextParameter(
        Type[] declaringTypes,
        params string[] methodNames)
    {
        var methodNameSet = new HashSet<string>(methodNames, StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (Type declaringType in declaringTypes)
        {
            MethodInfo[] methods = declaringType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (MethodInfo method in methods)
            {
                if (method.IsPrivate || !methodNameSet.Contains(method.Name))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(TrailblazerWorldContext))
                    violations.Add($"{declaringType.Name}.{method.Name} lacks an explicit first TrailblazerWorldContext parameter");
            }
        }

        violations.Sort(StringComparer.Ordinal);
        return violations;
    }

    private static IEnumerable<string> EnumerateProductionSourceFiles(string sourceRoot)
    {
        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string NormalizeRelativeSourcePath(string path) =>
        path.Replace('\\', '/');

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
