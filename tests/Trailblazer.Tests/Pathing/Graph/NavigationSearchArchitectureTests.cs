//=======================================================================
// NavigationSearchArchitectureTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationSearchArchitectureTests
{
    private static readonly string[] ReviewedAStarFiles =
    {
        "NavigationAStarAdmissionGate.cs",
        "NavigationAStarGuideLease.cs",
        "NavigationAStarNodeTable.cs",
        "NavigationAStarPayload.cs",
        "NavigationAStarPayloadCache.cs",
        "NavigationAStarPayloadKey.cs",
        "NavigationAStarPayloadLease.cs",
        "NavigationAStarQueryWork.cs",
        "NavigationAStarWorkspace.cs",
        "NavigationDependencySortWork.cs",
        "NavigationDependencyStampWork.cs",
        "NavigationEndpointResolutionWork.cs",
        "NavigationGuideLease.cs",
        "NavigationPageStampSet.cs",
        "NavigationQueryAdmissionWork.cs",
        "NavigationResolvedPathQuery.cs",
        "NavigationSurfaceAStarWork.cs"
    };

    private static readonly string[] ReviewedSupportingFiles =
    {
        "Pathing/Graph/NavigationDistanceMath.cs",
        "Pathing/Graph/NavigationSurfaceEdgeEnumerator.cs",
        "Pathing/Graph/TraversalEvaluator.cs",
        "Pathing/Query/NavigationQueryLimits.cs",
        "Pathing/Query/PathQueryBatch.cs"
    };

    private static readonly string[] BannedIdentifiers =
    {
        "AStarSurveyor",
        "ChartInterval",
        "GridStorageKind",
        "NavigationChart",
        "PathGuideFactory",
        "PathHeap",
        "PathManager",
        "Partition",
        "RectangularDirection",
        "ReusableSurveyResultCache",
        "SolidChart",
        "SolidVoxelFinder",
        "VoxelFinder",
        "WorldVoxelIndex"
    };

    [Fact]
    public void ReviewedNavigationSearchSlice_ShouldRejectUnreviewedFilesAndLegacyDependencies()
    {
        string sourceRoot = GetSourceRoot();
        string aStarRoot = Path.Combine(sourceRoot, "Pathing", "Search", "AStar");
        string[] actualNavigationFiles = Directory
            .GetFiles(aStarRoot, "Navigation*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()!;
        string[] expectedNavigationFiles = ReviewedAStarFiles
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        actualNavigationFiles.Should().Equal(
            expectedNavigationFiles,
            "every new search source must be deliberately reviewed and added to the allowlist");

        var reviewedPaths = new List<string>(ReviewedAStarFiles.Length + ReviewedSupportingFiles.Length);
        for (int i = 0; i < ReviewedAStarFiles.Length; i++)
            reviewedPaths.Add(Path.Combine(aStarRoot, ReviewedAStarFiles[i]));
        for (int i = 0; i < ReviewedSupportingFiles.Length; i++)
        {
            reviewedPaths.Add(Path.Combine(
                sourceRoot,
                ReviewedSupportingFiles[i].Replace('/', Path.DirectorySeparatorChar)));
        }

        var violations = new List<string>();
        for (int fileIndex = 0; fileIndex < reviewedPaths.Count; fileIndex++)
        {
            string path = reviewedPaths[fileIndex];
            File.Exists(path).Should().BeTrue($"reviewed source {path} must remain present");
            string source = File.ReadAllText(path);
            for (int identifierIndex = 0;
                identifierIndex < BannedIdentifiers.Length;
                identifierIndex++)
            {
                string identifier = BannedIdentifiers[identifierIndex];
                if (source.Contains(identifier, StringComparison.Ordinal))
                    violations.Add($"{Path.GetFileName(path)} -> {identifier}");
            }
        }

        violations.Should().BeEmpty(
            "the new endpoint/evaluator/A* slice cannot regain charts, partitions, legacy providers, old finders, or rectangular-only search dependencies");
    }

    private static string GetSourceRoot([CallerFilePath] string testFile = "")
    {
        string graphTests = Path.GetDirectoryName(testFile)!;
        string repository = Path.GetFullPath(Path.Combine(graphTests, "..", "..", "..", ".."));
        return Path.Combine(repository, "src", "Trailblazer");
    }
}
