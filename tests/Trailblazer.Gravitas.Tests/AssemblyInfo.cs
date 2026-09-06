using Xunit;

// Joint tests exercise both libraries' shared pools and static runtime state.
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
