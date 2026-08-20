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
        "NavigationAStarGuidePoint.cs",
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
        "Pathing/Graph/NavigationSurfaceEdgeRouteWork.cs",
        "Pathing/Graph/NavigationSurfaceEdgeEnumerator.cs",
        "Pathing/Graph/TraversalEvaluator.cs",
        "Pathing/Query/NavigationQueryLimits.cs",
        "Pathing/Query/PathQueryBatch.cs",
        "Pathing/Search/Flow/GuideSampleBatch.cs",
        "Pathing/Search/Flow/GuideSampleWorkMeter.cs",
        "Pathing/Search/NavigationEndpointWorkspace.cs",
        "Pathing/Search/NavigationQueryAdmissionCoordinator.cs"
    };

    private static readonly string[] ReviewedFlowFiles =
    {
        "NavigationFlowAdmissionGate.cs",
        "NavigationFlowBatchWork.cs",
        "NavigationFlowFieldGuideLease.cs",
        "NavigationFlowFieldLease.cs",
        "NavigationFlowFieldNode.cs",
        "NavigationFlowFieldOpenHeap.cs",
        "NavigationFlowFieldPayload.cs",
        "NavigationFlowFieldPayloadCache.cs",
        "NavigationFlowFieldPayloadKey.cs",
        "NavigationFlowFieldPayloadLease.cs",
        "NavigationFlowFieldStatus.cs",
        "NavigationFlowFieldWork.cs",
        "NavigationFlowFieldWorkspace.cs",
        "NavigationFlowQueryWork.cs",
        "NavigationSelectedEdgeProgressWork.cs"
    };

    private static readonly string[] BannedIdentifiers =
    {
        "AStarSurveyor",
        "ChartInterval",
        "GridNavigationCorridorValidationCursor",
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
        string flowRoot = Path.Combine(sourceRoot, "Pathing", "Search", "Flow");
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
        string[] actualFlowFiles = Directory
            .GetFiles(flowRoot, "Navigation*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()!;
        actualFlowFiles.Should().Equal(
            ReviewedFlowFiles.OrderBy(name => name, StringComparer.Ordinal),
            "every new flow source must be deliberately reviewed and added to the allowlist");

        var reviewedPaths = new List<string>(
            ReviewedAStarFiles.Length
            + ReviewedFlowFiles.Length
            + ReviewedSupportingFiles.Length);
        for (int i = 0; i < ReviewedAStarFiles.Length; i++)
            reviewedPaths.Add(Path.Combine(aStarRoot, ReviewedAStarFiles[i]));
        for (int i = 0; i < ReviewedSupportingFiles.Length; i++)
        {
            reviewedPaths.Add(Path.Combine(
                sourceRoot,
                ReviewedSupportingFiles[i].Replace('/', Path.DirectorySeparatorChar)));
        }
        for (int i = 0; i < ReviewedFlowFiles.Length; i++)
            reviewedPaths.Add(Path.Combine(flowRoot, ReviewedFlowFiles[i]));

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

    [Fact]
    public void ExplicitRefresh_ShouldRetainValidatedCursorPortalsWithoutReconstruction()
    {
        string explicitRefresh = Path.Combine(
            GetSourceRoot(),
            "Pathing",
            "Map",
            "Operations",
            "NavigationOperationCandidate.ExplicitConnections.cs");
        string source = File.ReadAllText(explicitRefresh);

        source.Contains(
                "GridCellGeometry.TryCreateNavigationPortal(",
                StringComparison.Ordinal)
            .Should().BeFalse(
                "corridor validation must emit the exact retained certificates instead of "
                + "triggering a second geometry pass");
        source.Should().Contain(
            "maxWork: 1",
            "the cursor exposes only the portal emitted by its final completed work unit");
        source.Should().Contain(
            "_corridorCursor.TryGetCurrentPortal",
            "every one-unit cursor advance must consume its emitted certificate immediately");
    }

    [Fact]
    public void NavigationRay_ShouldConsumeOneExplicitEvidenceLoopWithoutRetainingTheEdgeTwice()
    {
        string sourceRoot = GetSourceRoot();
        string raySource = File.ReadAllText(Path.Combine(
            sourceRoot,
            "Pathing",
            "Search",
            "Ray",
            "NavigationRayWork.cs"));
        string workspaceSource = File.ReadAllText(Path.Combine(
            sourceRoot,
            "Pathing",
            "Search",
            "Ray",
            "NavigationRayWorkspace.cs"));
        string evaluatorSource = File.ReadAllText(Path.Combine(
            sourceRoot,
            "Pathing",
            "Graph",
            "TraversalEvaluator.cs"));

        raySource.Split("BeginExplicitEdge(", StringSplitOptions.None)
            .Length.Should().Be(2,
                "the ray must have one semantic and geometric explicit-edge loop");
        raySource.Split("AdvanceExplicitEdge(", StringSplitOptions.None)
            .Length.Should().Be(2,
                "each explicit corridor leg must emit evidence exactly once");
        raySource.Should().NotContain(
            "TryGetExplicitTraversal(",
            "the ray must not reconstruct explicit traversal after semantic evaluation");
        raySource.Split(".NavigationPortals", StringSplitOptions.None)
            .Length.Should().Be(2,
                "only the reached target's final incoming certificate may be indexed");
        raySource.Should().Contain(
            "portal = portals[portals.Count - 1];",
            "the immutable final certificate is an O(1) indexed read, not a corridor pass");
        raySource.Should().NotContain(
            ".NavigationPortals.GetEnumerator()",
            "the ray must never enumerate an explicit corridor a second time");
        workspaceSource.Should().NotContain(
            "IncomingExplicitPortal",
            "one retained portal per chain slot multiplies across every ray workspace");
        evaluatorSource.Should().NotContain(
            "internal NavigationGraphEdge Edge;",
            "route work already retains the edge; explicit traversal needs only its record and target");
    }

    [Fact]
    public void FlowRecoveryPostAdvanceRead_ShouldKeepStaleRecoveryOnly()
    {
        string steeringPath = Path.Combine(
            GetSourceRoot(),
            "Navigation",
            "Steering",
            "NavSteering.Simulation.cs");
        string source = File.ReadAllText(steeringPath);
        int methodIndex = source.IndexOf(
            "private bool TryGetFlowRecoveryHeading",
            StringComparison.Ordinal);
        int advanceIndex = source.IndexOf(
            "status = guide.TryAdvanceWaypoint();",
            methodIndex,
            StringComparison.Ordinal);
        int readIndex = source.IndexOf(
            "status = guide.TryGetCurrentWaypoint(out _, out waypoint);",
            advanceIndex,
            StringComparison.Ordinal);
        int headingIndex = source.IndexOf(
            "heading = waypoint - position;",
            readIndex,
            StringComparison.Ordinal);

        methodIndex.Should().BeGreaterThanOrEqualTo(0);
        advanceIndex.Should().BeGreaterThan(methodIndex);
        readIndex.Should().BeGreaterThan(advanceIndex);
        headingIndex.Should().BeGreaterThan(readIndex);
        string postAdvanceRead = source.Substring(readIndex, headingIndex - readIndex);
        postAdvanceRead.Should().Contain("status == NavigationGuideStatus.Stale");
        postAdvanceRead.Should().Contain("_flowRecoveryGuideLease?.Dispose();");
        postAdvanceRead.Should().Contain("_flowRecoveryGuideLease = null;");
        postAdvanceRead.Should().Contain("return true;");
        postAdvanceRead.Should().NotContain("_navigationFlowFieldLease");
        postAdvanceRead.Should().NotContain("_currentQuery");
        postAdvanceRead.Should().NotContain("PreparePathRetry");
        postAdvanceRead.Should().NotContain("HandleInvalidPath");
        postAdvanceRead.Should().NotContain("ReleaseNavigationGuidance");
    }

    [Fact]
    public void EndpointProofPublication_ShouldCloseWorldSequenceAndAvoidReranking()
    {
        string endpointPath = Path.Combine(
            GetSourceRoot(),
            "Pathing",
            "Search",
            "AStar",
            "NavigationEndpointResolutionWork.cs");
        string source = File.ReadAllText(endpointPath);
        int validationIndex = source.IndexOf(
            "private bool AreDependenciesCurrent()",
            StringComparison.Ordinal);
        validationIndex.Should().BeGreaterThanOrEqualTo(0);
        string validation = source.Substring(validationIndex);

        validation.Split("_world.ChangeSequence", StringSplitOptions.None)
            .Length.Should().Be(3,
                "world identity must bracket the dependency scan");
        source.Should().NotContain(
            "if (!CanBeatCurrentResult(candidate.Address, candidate.ResolutionDistance))",
            "candidate ranking was already proved before the ray began");
    }

    [Fact]
    public void AStarQuerySlot_ShouldConstructOneRayWorkInAdmissionOnly()
    {
        string root = Path.Combine(
            GetSourceRoot(),
            "Pathing",
            "Search",
            "AStar");
        string admission = File.ReadAllText(Path.Combine(
            root,
            "NavigationQueryAdmissionWork.cs"));
        string endpoint = File.ReadAllText(Path.Combine(
            root,
            "NavigationEndpointResolutionWork.cs"));
        string workspace = File.ReadAllText(Path.Combine(
            root,
            "NavigationAStarWorkspace.cs"));

        admission.Split("new NavigationRayWork(", StringSplitOptions.None)
            .Length.Should().Be(2,
                "one admission-owned ray is shared with endpoint resolution and search");
        endpoint.Should().NotContain("new NavigationRayWork(");
        workspace.Should().NotContain("new NavigationRayWork(");
    }

    [Fact]
    public void ConditionalWorldEpoch_ShouldBeTheTrailingPublicationAndGuideValidation()
    {
        string root = Path.Combine(
            GetSourceRoot(),
            "Pathing",
            "Search",
            "AStar");
        string cache = File.ReadAllText(Path.Combine(
            root,
            "NavigationAStarPayloadCache.cs"));
        string guide = File.ReadAllText(Path.Combine(
            root,
            "NavigationAStarGuideLease.cs"));
        string query = File.ReadAllText(Path.Combine(
            root,
            "NavigationAStarQueryWork.cs"));

        cache.Should().Contain(
            "graph.IsDependencyCurrent(current.Payload.Dependencies)\n                && IsWorldCurrent(current.Payload)");
        cache.Should().Contain(
            "!graphLease.Graph.IsDependencyCurrent(payload.Dependencies)\n                || !store.Current.IsDependencyCurrent(payload.Dependencies)\n                || !IsWorldCurrent(payload)");
        guide.Should().Contain(
            "!store.Current.IsDependencyCurrent(payload.Dependencies)\n                || !_owner.IsWorldCurrent(payload)");

        int publish = query.IndexOf("_cache.TryPublish(", StringComparison.Ordinal);
        int trailingStore = query.IndexOf(
            "_store.Current.IsDependencyCurrent(published.Payload.Dependencies)",
            publish,
            StringComparison.Ordinal);
        int trailingWorld = query.IndexOf(
            "_cache.IsWorldCurrent(published.Payload)",
            trailingStore,
            StringComparison.Ordinal);
        int removal = query.IndexOf(
            "_cache.RemoveExact(published.Payload)",
            trailingWorld,
            StringComparison.Ordinal);
        publish.Should().BeGreaterThanOrEqualTo(0);
        trailingStore.Should().BeGreaterThan(publish);
        trailingWorld.Should().BeGreaterThan(trailingStore);
        removal.Should().BeGreaterThan(trailingWorld);
    }

    private static string GetSourceRoot([CallerFilePath] string testFile = "")
    {
        string graphTests = Path.GetDirectoryName(testFile)!;
        string repository = Path.GetFullPath(Path.Combine(graphTests, "..", "..", "..", ".."));
        return Path.Combine(repository, "src", "Trailblazer");
    }
}
