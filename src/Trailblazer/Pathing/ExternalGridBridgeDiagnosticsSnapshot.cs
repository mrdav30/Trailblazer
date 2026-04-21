namespace Trailblazer.Pathing;

/// <summary>
/// Immutable counters describing observed external-grid bridge activity inside <see cref="PathManager"/>.
/// </summary>
internal readonly struct ExternalGridBridgeDiagnosticsSnapshot
{
    /// <summary>
    /// Initializes a new diagnostics snapshot.
    /// </summary>
    internal ExternalGridBridgeDiagnosticsSnapshot(
        int totalGridEventsReceived,
        int addedEventsReceived,
        int removedEventsReceived,
        int changedEventsReceived,
        int distinctGridSlotsObserved,
        int duplicateEventSignaturesObserved,
        int duplicateAddEventSignaturesObserved,
        int duplicateRemoveEventSignaturesObserved,
        int duplicateChangeEventSignaturesObserved,
        int maxIdenticalEventStreak,
        int rebuildPassesExecuted,
        int eventsIgnoredForNoIntersectingCharts,
        int totalChartsSelectedForRebuild,
        int maxChartsSelectedForSingleEvent)
    {
        TotalGridEventsReceived = totalGridEventsReceived;
        AddedEventsReceived = addedEventsReceived;
        RemovedEventsReceived = removedEventsReceived;
        ChangedEventsReceived = changedEventsReceived;
        DistinctGridSlotsObserved = distinctGridSlotsObserved;
        DuplicateEventSignaturesObserved = duplicateEventSignaturesObserved;
        DuplicateAddEventSignaturesObserved = duplicateAddEventSignaturesObserved;
        DuplicateRemoveEventSignaturesObserved = duplicateRemoveEventSignaturesObserved;
        DuplicateChangeEventSignaturesObserved = duplicateChangeEventSignaturesObserved;
        MaxIdenticalEventStreak = maxIdenticalEventStreak;
        RebuildPassesExecuted = rebuildPassesExecuted;
        EventsIgnoredForNoIntersectingCharts = eventsIgnoredForNoIntersectingCharts;
        TotalChartsSelectedForRebuild = totalChartsSelectedForRebuild;
        MaxChartsSelectedForSingleEvent = maxChartsSelectedForSingleEvent;
    }

    public int TotalGridEventsReceived { get; }

    public int AddedEventsReceived { get; }

    public int RemovedEventsReceived { get; }

    public int ChangedEventsReceived { get; }

    public int DistinctGridSlotsObserved { get; }

    public int DuplicateEventSignaturesObserved { get; }

    public int DuplicateAddEventSignaturesObserved { get; }

    public int DuplicateRemoveEventSignaturesObserved { get; }

    public int DuplicateChangeEventSignaturesObserved { get; }

    /// <summary>
    /// Gets the largest consecutive run of identical signatures observed for a single grid slot.
    /// A value of zero means no grid events have been recorded yet.
    /// </summary>
    public int MaxIdenticalEventStreak { get; }

    public int RebuildPassesExecuted { get; }

    public int EventsIgnoredForNoIntersectingCharts { get; }

    public int TotalChartsSelectedForRebuild { get; }

    public int MaxChartsSelectedForSingleEvent { get; }
}
