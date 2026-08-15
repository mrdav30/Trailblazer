//=======================================================================
// NavigationGraphCompositionBenchmarks.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures one-map replacement and one bridge dependency mutation as context map count grows.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(Phase2GateConfig))]
[BenchmarkCategory("Phase2", "Graph", "Composition")]
public class NavigationGraphCompositionBenchmarks
{
    private GridWorld _world;
    private NavigationGraphRuntime _runtime;
    private NormalizedGridConfiguration _firstBinding;
    private NavigationCell _cell;
    private long _sequence;
    private long _bakeVersion;
    private int _frame;
    private int _bridgeMapIndex;
    private bool _bridgeSuppressed;
    private int _maximumConvergenceFrames;
    private int _maximumComponentNodes;
    private int _maximumExplicitEdges;
    private int _maximumDependencyEntries;
    private long _maximumActiveSnapshotBytes;
    private int _maximumPersistentGraphPages;
    private long _maximumCompositionWorkBytes;
    private int _maximumCompositionWorkPages;
    private long _maximumOperationWorkBytes;
    private int _maximumOperationWorkPages;
    private long _maximumTotalWorkBytes;
    private int _maximumTotalWorkPages;

    [Params(16, 128)]
    public int MapCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new GridWorld();
        _runtime = new NavigationGraphRuntime(_world, CreateCarryoverSettings());
        _cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        var operations = new NavigationMapCommitOperation[MapCount];
        for (int i = 0; i < MapCount; i++)
        {
            Vector3d origin = new(i * 4, 0, 0);
            var configuration = new GridConfiguration(
                origin,
                origin,
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Dense);
            _world.TryAddGrid(configuration, out _);
            configuration.TryNormalize(out NormalizedGridConfiguration binding);
            if (i == 0)
                _firstBinding = binding;
            NavigationMapBuilder builder = new NavigationMapBuilder($"map-{i:D3}", binding)
                .AddCell(default, _cell);
            if (i < MapCount - 1)
            {
                builder.AddTransition(new TraversalTransitionDefinition(
                    $"bridge-{i:D3}",
                    TraversalTransitionType.Climb,
                    default,
                    TraversalMedium.Solid,
                    new NavigationCellAddress($"map-{i + 1:D3}", default),
                    TraversalMedium.Solid,
                    TraversalCapability.Climb));
            }
            operations[i] = new NavigationMapCommitOperation(
                new PreparedNavigationMap(builder.Build(), 1),
                OverlayReplacementPolicy.Clear,
                ++_sequence,
                effectiveFrame: 1);
            _runtime.Admit(operations[i]);
        }
        _frame = 0;
        for (int i = 0; i < 4_096 && operations[MapCount - 1].Receipt.Status == NavigationOperationStatus.Pending; i++)
            _runtime.Maintain(++_frame);
        if (operations[MapCount - 1].Receipt.Status != NavigationOperationStatus.Applied)
            throw new InvalidOperationException("The giant-component setup did not converge.");
        _bridgeMapIndex = (MapCount - 1) / 2;
        _bakeVersion = 1;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine(
            $"PHASE2_STRUCTURAL maps={MapCount} max_frames={_maximumConvergenceFrames} "
            + $"component_nodes={_maximumComponentNodes} explicit_edges={_maximumExplicitEdges} "
            + $"dependency_entries={_maximumDependencyEntries} "
            + $"active_snapshot_bytes={_maximumActiveSnapshotBytes} "
            + $"persistent_graph_pages={_maximumPersistentGraphPages} "
            + $"composition_work_bytes={_maximumCompositionWorkBytes} "
            + $"composition_work_pages={_maximumCompositionWorkPages} "
            + $"operation_work_bytes={_maximumOperationWorkBytes} "
            + $"operation_work_pages={_maximumOperationWorkPages} "
            + $"total_work_bytes={_maximumTotalWorkBytes} "
            + $"total_work_pages={_maximumTotalWorkPages}");
        _runtime.Dispose();
        _world.Dispose();
    }

    [Benchmark(Baseline = true)]
    public long ReplaceOneMap()
    {
        NavigationMap map = new NavigationMapBuilder("map-000", _firstBinding)
            .AddCell(default, _cell)
            .AddTransition(new TraversalTransitionDefinition(
                "bridge-000",
                TraversalTransitionType.Climb,
                default,
                TraversalMedium.Solid,
                new NavigationCellAddress("map-001", default),
                TraversalMedium.Solid,
                TraversalCapability.Climb))
            .Build();
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, ++_bakeVersion),
            OverlayReplacementPolicy.PreserveAndRevalidate,
            ++_sequence,
            _frame + 1);
        _runtime.Admit(operation);
        return MaintainUntilTerminal(operation.Receipt);
    }

    [Benchmark]
    public long ToggleMiddleBridgeAndConverge()
    {
        string transitionId = $"bridge-{_bridgeMapIndex:D3}";
        TraversalTransitionOverlayOperation transition = _bridgeSuppressed
            ? TraversalTransitionOverlayOperation.RevertToBake(transitionId)
            : TraversalTransitionOverlayOperation.Suppress(transitionId);
        _bridgeSuppressed = !_bridgeSuppressed;
        var transaction = new NavigationOverlayTransaction(
            new[]
            {
                new NavigationMapOverlayDelta(
                    $"map-{_bridgeMapIndex:D3}",
                    transitions: new[] { transition })
            });
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(transaction),
            ++_sequence,
            _frame + 1);
        _runtime.Admit(operation);
        return MaintainUntilTerminal(operation.Receipt);
    }

    private long MaintainUntilTerminal(NavigationOperationReceipt receipt)
    {
        int frames = 0;
        int componentNodes = 0;
        int explicitEdges = 0;
        int dependencyEntries = 0;
        while (receipt.Status == NavigationOperationStatus.Pending && frames < 4_096)
        {
            _runtime.Maintain(++_frame);
            frames++;
            componentNodes += _runtime.MaintenanceMeter.ComponentNodes;
            explicitEdges += _runtime.MaintenanceMeter.ExplicitEdges;
            dependencyEntries += _runtime.MaintenanceMeter.DependencyEntries;
            if (_runtime.RetainedCompositionWorkCount != 0
                || _runtime.RetainedOperationWorkCount != 0)
            {
                NavigationGraphDiagnosticsSnapshot diagnostics = _runtime.GetDiagnostics(0);
                _maximumActiveSnapshotBytes = Math.Max(
                    _maximumActiveSnapshotBytes,
                    diagnostics.ActiveSnapshotBytes);
                _maximumPersistentGraphPages = Math.Max(
                    _maximumPersistentGraphPages,
                    diagnostics.PersistentGraphPageCount);
                _maximumCompositionWorkBytes = Math.Max(
                    _maximumCompositionWorkBytes,
                    _runtime.RetainedCompositionWorkBytes);
                _maximumCompositionWorkPages = Math.Max(
                    _maximumCompositionWorkPages,
                    _runtime.RetainedCompositionWorkPageCount);
                _maximumOperationWorkBytes = Math.Max(
                    _maximumOperationWorkBytes,
                    _runtime.RetainedOperationWorkBytes);
                _maximumOperationWorkPages = Math.Max(
                    _maximumOperationWorkPages,
                    _runtime.RetainedOperationWorkPageCount);
                _maximumTotalWorkBytes = Math.Max(
                    _maximumTotalWorkBytes,
                    checked(_runtime.RetainedCompositionWorkBytes
                        + _runtime.RetainedOperationWorkBytes));
                _maximumTotalWorkPages = Math.Max(
                    _maximumTotalWorkPages,
                    checked(_runtime.RetainedCompositionWorkPageCount
                        + _runtime.RetainedOperationWorkPageCount));
            }
        }
        if (receipt.Status != NavigationOperationStatus.Applied)
            throw new InvalidOperationException("Structural composition did not converge.");
        _maximumConvergenceFrames = Math.Max(_maximumConvergenceFrames, frames);
        _maximumComponentNodes = Math.Max(_maximumComponentNodes, componentNodes);
        _maximumExplicitEdges = Math.Max(_maximumExplicitEdges, explicitEdges);
        _maximumDependencyEntries = Math.Max(_maximumDependencyEntries, dependencyEntries);
        return _runtime.Current.GraphVersion;
    }

    private static TrailblazerWorldContextSettings CreateCarryoverSettings()
    {
        TrailblazerWorldContextSettings defaults = NavigationGraphLifecycleBenchmarks.CreateSettings();
        int maximumRetainedAreaPolicies = Math.Min(
            defaults.MaxAreaPolicies,
            defaults.MaxAreaRules / defaults.NavigationAreaCount);
        int minimumAreaPolicyPublicationWork = Math.Max(
            defaults.NavigationAreaCount,
            2 * maximumRetainedAreaPolicies) + 1;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes: 8,
            maxSeamCandidateProbes: 8,
            maxExplicitEdges: 8,
            maxDependencyEntries: minimumAreaPolicyPublicationWork);
        return new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            defaults.NavigationAreaCount,
            defaults.MaxAreaPolicies,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
    }
}
