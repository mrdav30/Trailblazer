using System;
using System.Threading;
using BenchmarkDotNet.Attributes;

namespace Trailblazer.Benchmarks.Phase0;

/// <summary>
/// Synthetic Phase 0 comparison of global guide invalidation under a reader/writer
/// lock and immutable component-version publication under streamed grid churn.
/// </summary>
/// <remarks>
/// This isolates publication and dependency fan-out. It does not model graph
/// composition, search, or contended reader scheduling.
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("Phase0", "Streaming", "Invalidation")]
public class StreamedGridChurnBenchmarks
{
    private const int ComponentCount = 128;
    private const int ComponentsPerPage = 32;
    private const int PageCount = ComponentCount / ComponentsPerPage;
    private const int MaximumEventsPerBatch = 64;

    private ReaderWriterLockSlim _globalGate;
    private object _snapshotGate;
    private int[] _globalGuideVersions;
    private int[] _snapshotGuideVersions;
    private int[][] _guidesByComponent;
    private int[] _eventComponents;
    private int[] _changedComponents;
    private int[] _changedPages;
    private int[] _componentMarks;
    private int[] _pageMarks;
    private ComponentVersionSnapshot _snapshot;
    private int _globalVersion;
    private int _snapshotVersion;
    private int _eventCursor;
    private int _markEpoch;

    /// <summary>Number of active guides that can be invalidated.</summary>
    [Params(500, 5_000)]
    public int GuideCount { get; set; }

    /// <summary>Number of distinct streamed mutations processed by one benchmark operation.</summary>
    [Params(1, MaximumEventsPerBatch)]
    public int EventsPerBatch { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _globalGate = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        _snapshotGate = new object();
        _globalGuideVersions = new int[GuideCount];
        _snapshotGuideVersions = new int[GuideCount];
        _guidesByComponent = BuildGuideDependencies(GuideCount);
        _eventComponents = new int[MaximumEventsPerBatch];
        _changedComponents = new int[ComponentCount];
        _changedPages = new int[PageCount];
        _componentMarks = new int[ComponentCount];
        _pageMarks = new int[PageCount];
        _snapshot = ComponentVersionSnapshot.Create(ComponentCount, ComponentsPerPage);

        for (int i = 0; i < MaximumEventsPerBatch; i++)
            _eventComponents[i] = (i * 37) & (ComponentCount - 1);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _globalGate?.Dispose();
    }

    /// <summary>
    /// Models immediate event delivery: every mutation enters the writer lock and
    /// invalidates every active guide.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int GlobalRwLock_PerEventGlobalInvalidation()
    {
        int invalidationTouches = 0;

        for (int i = 0; i < EventsPerBatch; i++)
        {
            _globalGate.EnterWriteLock();
            try
            {
                int version = ++_globalVersion;
                Array.Fill(_globalGuideVersions, version);
                invalidationTouches += GuideCount;
            }
            finally
            {
                _globalGate.ExitWriteLock();
            }
        }

        AdvanceEventCursor();
        return invalidationTouches;
    }

    /// <summary>
    /// Publishes one immutable paged component-version snapshot per event and
    /// invalidates only guides depending on the changed component.
    /// </summary>
    [Benchmark]
    public int ImmutableSnapshot_PerEventStructuralInvalidation()
    {
        int invalidationTouches = 0;

        for (int i = 0; i < EventsPerBatch; i++)
        {
            int component = EventComponentAt(i);
            lock (_snapshotGate)
            {
                _snapshot = _snapshot.WithIncrementedComponent(component, ComponentsPerPage);
                int version = ++_snapshotVersion;
                int[] dependentGuides = _guidesByComponent[component];
                for (int guideIndex = 0; guideIndex < dependentGuides.Length; guideIndex++)
                    _snapshotGuideVersions[dependentGuides[guideIndex]] = version;

                invalidationTouches += dependentGuides.Length;
            }
        }

        AdvanceEventCursor();
        return invalidationTouches;
    }

    /// <summary>
    /// Models deterministic maintenance-prefix processing: clone each touched page
    /// once, publish once, and invalidate the union of affected dependencies.
    /// </summary>
    [Benchmark]
    public int ImmutableSnapshot_BatchedStructuralInvalidation()
    {
        int epoch = NextMarkEpoch();
        int changedComponentCount = 0;
        int changedPageCount = 0;

        for (int i = 0; i < EventsPerBatch; i++)
        {
            int component = EventComponentAt(i);
            if (_componentMarks[component] != epoch)
            {
                _componentMarks[component] = epoch;
                _changedComponents[changedComponentCount++] = component;
            }

            int page = component / ComponentsPerPage;
            if (_pageMarks[page] != epoch)
            {
                _pageMarks[page] = epoch;
                _changedPages[changedPageCount++] = page;
            }
        }

        int invalidationTouches = 0;
        lock (_snapshotGate)
        {
            _snapshot = _snapshot.WithIncrementedComponents(
                _changedComponents,
                changedComponentCount,
                _changedPages,
                changedPageCount,
                ComponentsPerPage);

            int version = ++_snapshotVersion;
            for (int changedIndex = 0; changedIndex < changedComponentCount; changedIndex++)
            {
                int[] dependentGuides = _guidesByComponent[_changedComponents[changedIndex]];
                for (int guideIndex = 0; guideIndex < dependentGuides.Length; guideIndex++)
                    _snapshotGuideVersions[dependentGuides[guideIndex]] = version;

                invalidationTouches += dependentGuides.Length;
            }
        }

        AdvanceEventCursor();
        return invalidationTouches;
    }

    private static int[][] BuildGuideDependencies(int guideCount)
    {
        var counts = new int[ComponentCount];
        for (int guide = 0; guide < guideCount; guide++)
            counts[guide & (ComponentCount - 1)]++;

        var guidesByComponent = new int[ComponentCount][];
        for (int component = 0; component < ComponentCount; component++)
            guidesByComponent[component] = new int[counts[component]];

        Array.Clear(counts, 0, counts.Length);
        for (int guide = 0; guide < guideCount; guide++)
        {
            int component = guide & (ComponentCount - 1);
            guidesByComponent[component][counts[component]++] = guide;
        }

        return guidesByComponent;
    }

    private int EventComponentAt(int index)
    {
        return (_eventComponents[index] + _eventCursor) & (ComponentCount - 1);
    }

    private void AdvanceEventCursor()
    {
        _eventCursor = (_eventCursor + EventsPerBatch) & (ComponentCount - 1);
    }

    private int NextMarkEpoch()
    {
        if (_markEpoch == int.MaxValue)
        {
            Array.Clear(_componentMarks, 0, _componentMarks.Length);
            Array.Clear(_pageMarks, 0, _pageMarks.Length);
            _markEpoch = 0;
        }

        return ++_markEpoch;
    }

    private sealed class ComponentVersionSnapshot
    {
        private ComponentVersionSnapshot(int[][] pages)
        {
            Pages = pages;
        }

        private int[][] Pages { get; }

        public static ComponentVersionSnapshot Create(int componentCount, int componentsPerPage)
        {
            int pageCount = (componentCount + componentsPerPage - 1) / componentsPerPage;
            var pages = new int[pageCount][];
            for (int page = 0; page < pageCount; page++)
                pages[page] = new int[componentsPerPage];

            return new ComponentVersionSnapshot(pages);
        }

        public ComponentVersionSnapshot WithIncrementedComponent(int component, int componentsPerPage)
        {
            int page = component / componentsPerPage;
            int offset = component % componentsPerPage;
            var pages = (int[][])Pages.Clone();
            var changedPage = (int[])pages[page].Clone();
            changedPage[offset]++;
            pages[page] = changedPage;
            return new ComponentVersionSnapshot(pages);
        }

        public ComponentVersionSnapshot WithIncrementedComponents(
            int[] changedComponents,
            int changedComponentCount,
            int[] changedPages,
            int changedPageCount,
            int componentsPerPage)
        {
            var pages = (int[][])Pages.Clone();
            for (int i = 0; i < changedPageCount; i++)
            {
                int page = changedPages[i];
                pages[page] = (int[])pages[page].Clone();
            }

            for (int i = 0; i < changedComponentCount; i++)
            {
                int component = changedComponents[i];
                pages[component / componentsPerPage][component % componentsPerPage]++;
            }

            return new ComponentVersionSnapshot(pages);
        }
    }
}
