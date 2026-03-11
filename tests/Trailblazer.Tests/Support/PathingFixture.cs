using GridForge;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests;

public class PathingFixture : IDisposable
{
    public PathingFixture()
    {
        GridForgeLogger.Verbosity = GridForgeLogger.LogLevel.Error;
        GlobalGridManager.Setup();
    }

    public void Dispose()
    {
        PathManager.UnloadAllCharts();
        PathManager.ClearAll();

        GlobalGridManager.Reset();
        TrailblazerManager.Reset();

        GC.SuppressFinalize(this);
    }
}

[CollectionDefinition("PathingCollection")]
public class PathingCollection : ICollectionFixture<PathingFixture> { }
