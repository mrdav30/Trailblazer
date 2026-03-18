using Xunit;

// Trailblazer tests share global runtime state through static managers, so
// collection-level parallel execution can cause cross-test interference.
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
