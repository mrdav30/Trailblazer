//=======================================================================
// NavigationAcquisitionDiagnostics.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>
/// Context-local cumulative acquisition accounting; never controls simulation decisions.
/// Individual reads are atomic, but a multi-counter observation requires quiescent query workers.
/// Counts are never reset. Meter totals include finalized or cancelled admitted queries, not
/// immediate LOS/guide sampling, unmetered clearing/copy work, or rejected gate admissions.
/// </summary>
internal sealed class NavigationAcquisitionDiagnostics
{
    private long _buildStarts;
    private long _cacheCheckoutHits;
    private long _promotions;
    private long _sharedFollowerCheckouts;
    private long _sharedCoverageMisses;
    private long _completedQueries;
    private long _lookupProbes;
    private long _endpointCandidates;
    private long _expandedNodes;
    private long _evaluatedEdges;
    private long _connectionLegs;
    private long _transitionCandidates;
    private long _transitionPairs;
    private long _primaryVolumeCandidates;
    private long _shortcutVolumeCandidates;
    private long _volumeUnionChecks;
    private long _successfulDependencyMerges;
    private long _traceIntervals;
    private long _coveredVoxelIntervals;
    private long _simplificationRays;

    internal long BuildStarts => Interlocked.Read(ref _buildStarts);
    internal long CacheCheckoutHits => Interlocked.Read(ref _cacheCheckoutHits);
    internal long Promotions => Interlocked.Read(ref _promotions);
    internal long SharedFollowerCheckouts => Interlocked.Read(ref _sharedFollowerCheckouts);
    internal long SharedCoverageMisses => Interlocked.Read(ref _sharedCoverageMisses);
    /// <summary>Queries finalized by success, failure, or cancellation disposal, each counted once.</summary>
    internal long CompletedQueries => Interlocked.Read(ref _completedQueries);
    internal long LookupProbes => Interlocked.Read(ref _lookupProbes);
    internal long EndpointCandidates => Interlocked.Read(ref _endpointCandidates);
    internal long ExpandedNodes => Interlocked.Read(ref _expandedNodes);
    internal long EvaluatedEdges => Interlocked.Read(ref _evaluatedEdges);
    internal long ConnectionLegs => Interlocked.Read(ref _connectionLegs);
    internal long TransitionCandidates => Interlocked.Read(ref _transitionCandidates);
    internal long TransitionPairs => Interlocked.Read(ref _transitionPairs);
    internal long PrimaryVolumeCandidates => Interlocked.Read(ref _primaryVolumeCandidates);
    internal long ShortcutVolumeCandidates => Interlocked.Read(ref _shortcutVolumeCandidates);
    internal long VolumeUnionChecks => Interlocked.Read(ref _volumeUnionChecks);
    internal long SuccessfulDependencyMerges => Interlocked.Read(ref _successfulDependencyMerges);
    internal long TraceIntervals => Interlocked.Read(ref _traceIntervals);
    internal long CoveredVoxelIntervals => Interlocked.Read(ref _coveredVoxelIntervals);
    internal long SimplificationRays => Interlocked.Read(ref _simplificationRays);

    internal void RecordBuildStart() => Interlocked.Increment(ref _buildStarts);
    internal void RecordCacheCheckoutHit() => Interlocked.Increment(ref _cacheCheckoutHits);
    internal void RecordPromotion() => Interlocked.Increment(ref _promotions);
    internal void RecordSharedFollowerCheckout() => Interlocked.Increment(ref _sharedFollowerCheckouts);
    internal void RecordSharedCoverageMiss() => Interlocked.Increment(ref _sharedCoverageMisses);

    internal void RecordCompletedQuery(NavigationWorkMeter meter)
    {
        Interlocked.Increment(ref _completedQueries);
        Interlocked.Add(ref _lookupProbes, meter.LookupProbes);
        Interlocked.Add(ref _endpointCandidates, meter.EndpointCandidates);
        Interlocked.Add(ref _expandedNodes, meter.ExpandedNodes);
        Interlocked.Add(ref _evaluatedEdges, meter.EvaluatedEdges);
        Interlocked.Add(ref _connectionLegs, meter.ConnectionLegs);
        Interlocked.Add(ref _transitionCandidates, meter.TransitionCandidates);
        Interlocked.Add(ref _transitionPairs, meter.TransitionPairs);
        Interlocked.Add(ref _primaryVolumeCandidates, meter.PrimaryVolumeCandidates);
        Interlocked.Add(ref _shortcutVolumeCandidates, meter.ShortcutVolumeCandidates);
        Interlocked.Add(ref _volumeUnionChecks, meter.VolumeUnionChecks);
        Interlocked.Add(ref _successfulDependencyMerges, meter.SuccessfulDependencyMerges);
        Interlocked.Add(ref _traceIntervals, meter.TraceIntervals);
        Interlocked.Add(ref _coveredVoxelIntervals, meter.CoveredVoxelIntervals);
        Interlocked.Add(ref _simplificationRays, meter.SimplificationRays);
    }
}
