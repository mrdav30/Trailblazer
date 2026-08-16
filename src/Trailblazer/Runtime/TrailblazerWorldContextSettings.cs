//=======================================================================
// TrailblazerWorldContextSettings.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Trailblazer.Pathing;

namespace Trailblazer;

/// <summary>
/// Freezes the finite graph, lifecycle, and query-workspace ceilings for one world context.
/// </summary>
public sealed class TrailblazerWorldContextSettings
{
    internal const int MinimumIngressBytes = 256;
    internal static readonly long MinimumActiveSnapshotBytes = checked(
        NavigationWorldGraph.Empty.RetainedBytes * 3L);
    internal static readonly int MinimumPersistentGraphPages = checked(
        NavigationWorldGraph.Empty.PersistentPageCount * 3);

    /// <summary>Gets the recommended finite settings used when a host does not provide custom limits.</summary>
    public static TrailblazerWorldContextSettings Default { get; } = CreateDefault();

    /// <summary>Creates an immutable explicit set of context-owned runtime ceilings.</summary>
    public TrailblazerWorldContextSettings(
        NavigationOperationLimits operationLimits,
        MaintenanceWorkBudget maintenanceBudget,
        GuideSampleWorkBudget guideSampleBudget,
        int maxIngressEntries,
        long maxIngressBytes,
        int maxActiveSnapshots,
        long maxActiveSnapshotBytes,
        int maxRetiredSnapshots,
        long maxRetiredSnapshotBytes,
        int maxPersistentGraphPages,
        int maxDynamicCellSlotsPerMap,
        int maxDynamicCellSlots,
        int navigationAreaCount,
        int maxAreaPolicies,
        int maxAreaRulesPerPolicy,
        int maxAreaRules,
        int maxConcurrentSnapshotLeases,
        NavigationQueryLimits queryLimits)
    {
        SwiftThrowHelper.ThrowIfArgument(
            operationLimits.MaxPendingOperations <= 0,
            nameof(operationLimits),
            "Operation limits must be explicitly initialized.");
        SwiftThrowHelper.ThrowIfArgument(
            maintenanceBudget.MaxConsumedEnvelopes <= 0,
            nameof(maintenanceBudget),
            "Maintenance work budget must be explicitly initialized.");
        ThrowIfNonPositive(maxIngressEntries, nameof(maxIngressEntries));
        ThrowIfNonPositive(maxIngressBytes, nameof(maxIngressBytes));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxIngressBytes < MinimumIngressBytes,
            null,
            nameof(maxIngressBytes));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxActiveSnapshots < 3,
            maxActiveSnapshots,
            nameof(maxActiveSnapshots),
            "Snapshot capacity must reserve the current, fail-closed, and candidate roots.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxActiveSnapshotBytes < MinimumActiveSnapshotBytes,
            null,
            nameof(maxActiveSnapshotBytes),
            "Snapshot bytes must reserve the current, fail-closed, and candidate empty roots.");
        ThrowIfNegative(maxRetiredSnapshots, nameof(maxRetiredSnapshots));
        ThrowIfNegative(maxRetiredSnapshotBytes, nameof(maxRetiredSnapshotBytes));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxPersistentGraphPages < MinimumPersistentGraphPages,
            maxPersistentGraphPages,
            nameof(maxPersistentGraphPages),
            "Persistent pages must reserve the current, fail-closed, and candidate empty roots.");
        ThrowIfNegative(maxDynamicCellSlotsPerMap, nameof(maxDynamicCellSlotsPerMap));
        ThrowIfNegative(maxDynamicCellSlots, nameof(maxDynamicCellSlots));
        SwiftThrowHelper.ThrowIfArgument(
            maxDynamicCellSlots < maxDynamicCellSlotsPerMap,
            nameof(maxDynamicCellSlots),
            "Context dynamic-cell capacity cannot be smaller than the per-map capacity.");
        SwiftThrowHelper.ThrowIfArgument(
            maxDynamicCellSlotsPerMap < operationLimits.MaxOverlayCellsPerMap
            || maxDynamicCellSlots < operationLimits.MaxOverlayCells,
            nameof(maxDynamicCellSlots),
            "Dynamic-cell capacity must cover every cell overlay admitted by the operation limits.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            navigationAreaCount <= 0 || navigationAreaCount > ushort.MaxValue + 1,
            navigationAreaCount,
            nameof(navigationAreaCount));
        ThrowIfNonPositive(maxAreaPolicies, nameof(maxAreaPolicies));
        ThrowIfNonPositive(maxAreaRulesPerPolicy, nameof(maxAreaRulesPerPolicy));
        ThrowIfNonPositive(maxAreaRules, nameof(maxAreaRules));
        SwiftThrowHelper.ThrowIfArgument(
            maxAreaRules < maxAreaRulesPerPolicy
            || maxAreaRulesPerPolicy < navigationAreaCount,
            nameof(maxAreaRules),
            "Area-rule capacity must cover the configured context area layout.");
        int maximumRetainedAreaPolicies = System.Math.Min(
            maxAreaPolicies,
            maxAreaRules / navigationAreaCount);
        long minimumAreaPolicyWork = System.Math.Max(
            navigationAreaCount,
            2L * maximumRetainedAreaPolicies) + 1L;
        SwiftThrowHelper.ThrowIfArgument(
            maintenanceBudget.MaxDependencyEntries < minimumAreaPolicyWork,
            nameof(maintenanceBudget),
            "Dependency work must fit one exact area-policy publication.");
        ThrowIfNonPositive(maxConcurrentSnapshotLeases, nameof(maxConcurrentSnapshotLeases));
        SwiftThrowHelper.ThrowIfArgument(
            queryLimits.MaxConcurrentNavigationQueries <= 0,
            nameof(queryLimits),
            "Query limits must be explicitly initialized.");
        SwiftThrowHelper.ThrowIfArgument(
            queryLimits.MaxConcurrentNavigationQueries > maxConcurrentSnapshotLeases,
            nameof(queryLimits),
            "Concurrent queries cannot exceed the context snapshot-lease ceiling.");

        OperationLimits = operationLimits;
        MaintenanceBudget = maintenanceBudget;
        GuideSampleBudget = guideSampleBudget;
        MaxIngressEntries = maxIngressEntries;
        MaxIngressBytes = maxIngressBytes;
        MaxActiveSnapshots = maxActiveSnapshots;
        MaxActiveSnapshotBytes = maxActiveSnapshotBytes;
        MaxRetiredSnapshots = maxRetiredSnapshots;
        MaxRetiredSnapshotBytes = maxRetiredSnapshotBytes;
        MaxPersistentGraphPages = maxPersistentGraphPages;
        MaxDynamicCellSlotsPerMap = maxDynamicCellSlotsPerMap;
        MaxDynamicCellSlots = maxDynamicCellSlots;
        NavigationAreaCount = navigationAreaCount;
        MaxAreaPolicies = maxAreaPolicies;
        MaxAreaRulesPerPolicy = maxAreaRulesPerPolicy;
        MaxAreaRules = maxAreaRules;
        MaxConcurrentSnapshotLeases = maxConcurrentSnapshotLeases;
        QueryLimits = queryLimits;
    }

    /// <summary>Gets the map-operation, preparation, and semantic-overlay ceilings.</summary>
    public NavigationOperationLimits OperationLimits { get; }

    /// <summary>Gets the deterministic work budget consumed at each maintenance boundary.</summary>
    public MaintenanceWorkBudget MaintenanceBudget { get; }

    /// <summary>Gets the deterministic work budget consumed by one graph flow guide sample.</summary>
    public GuideSampleWorkBudget GuideSampleBudget { get; }

    /// <summary>Gets the maximum coalesced GridForge ingress entries.</summary>
    public int MaxIngressEntries { get; }

    /// <summary>Gets the maximum retained GridForge ingress bytes.</summary>
    public long MaxIngressBytes { get; }

    /// <summary>Gets the maximum current, fail-closed, and unpublished active snapshots.</summary>
    public int MaxActiveSnapshots { get; }

    /// <summary>Gets the maximum bytes retained by active snapshots.</summary>
    public long MaxActiveSnapshotBytes { get; }

    /// <summary>Gets the maximum leased snapshot generations retained after publication.</summary>
    public int MaxRetiredSnapshots { get; }

    /// <summary>Gets the maximum bytes retained by leased retired snapshots.</summary>
    public long MaxRetiredSnapshotBytes { get; }

    /// <summary>Gets the maximum persistent registry, semantic, physical, and index pages.</summary>
    public int MaxPersistentGraphPages { get; }

    /// <summary>Gets the maximum non-reused dynamic cell slots retained by one map.</summary>
    public int MaxDynamicCellSlotsPerMap { get; }

    /// <summary>Gets the maximum non-reused dynamic cell slots retained by the context.</summary>
    public int MaxDynamicCellSlots { get; }

    /// <summary>Gets the exact contiguous context area layout spanning IDs zero through count minus one.</summary>
    public int NavigationAreaCount { get; }

    /// <summary>Gets the maximum immutable navigation-area policies retained by the context.</summary>
    public int MaxAreaPolicies { get; }

    /// <summary>Gets the maximum direct-indexed rules retained by one area policy.</summary>
    public int MaxAreaRulesPerPolicy { get; }

    /// <summary>Gets the maximum direct-indexed area rules retained across the context.</summary>
    public int MaxAreaRules { get; }

    /// <summary>Gets the maximum concurrently checked-out immutable graph snapshots.</summary>
    public int MaxConcurrentSnapshotLeases { get; }

    /// <summary>Gets the context-owned navigation query admission and retention ceilings.</summary>
    public NavigationQueryLimits QueryLimits { get; }

    private static TrailblazerWorldContextSettings CreateDefault() => new(
        new NavigationOperationLimits(
            maxPendingOperations: 32,
            maxPendingDescriptorBytes: 1_048_576,
            maxPreparedMapBytes: 16_777_216,
            maxBatchItems: 32,
            maxBatchDescriptorBytes: 1_048_576,
            maxBatchSortScratchBytes: 1_048_576,
            maxCorridorCells: 64,
            maxMaps: 16,
            maxRetainedMapIdentities: 32,
            maxOverlayCellsPerMap: 4_096,
            maxOverlayConnectionsPerMap: 1_024,
            maxOverlayTransitionsPerMap: 1_024,
            maxOverlayCells: 16_384,
            maxOverlayConnections: 4_096,
            maxOverlayTransitions: 4_096),
        new MaintenanceWorkBudget(
            maxConsumedEnvelopes: 4_096,
            maxBaselineAddresses: 65_536,
            maxOverlaySlots: 16_384,
            maxComponentNodes: 65_536,
            maxSeamCandidateProbes: 65_536,
            maxExplicitEdges: 65_536,
            maxDependencyEntries: 65_536,
            maxSurfaceComponentEdges: 65_536),
        new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 8,
            maxPortalChecks: 32,
            maxPrismChecks: 32,
            maxTraceIntervals: 32,
            maxLocalRecoveryAttempts: 1),
        maxIngressEntries: 16_384,
        maxIngressBytes: 4_194_304,
        maxActiveSnapshots: 3,
        maxActiveSnapshotBytes: 35_651_584,
        maxRetiredSnapshots: 8,
        maxRetiredSnapshotBytes: 67_108_864,
        maxPersistentGraphPages: 524_288,
        maxDynamicCellSlotsPerMap: 4_096,
        maxDynamicCellSlots: 16_384,
        navigationAreaCount: 1,
        maxAreaPolicies: 64,
        maxAreaRulesPerPolicy: 4_096,
        maxAreaRules: 65_536,
        maxConcurrentSnapshotLeases: 8,
        queryLimits: NavigationQueryLimits.Default);

    private static void ThrowIfNonPositive(int value, string parameterName) =>
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(value <= 0, value, parameterName);

    private static void ThrowIfNonPositive(long value, string parameterName) =>
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(value <= 0, null, parameterName);

    private static void ThrowIfNegative(int value, string parameterName) =>
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(value < 0, value, parameterName);

    private static void ThrowIfNegative(long value, string parameterName) =>
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(value < 0, null, parameterName);
}
