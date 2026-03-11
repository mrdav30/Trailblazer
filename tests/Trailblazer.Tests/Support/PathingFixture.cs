using GridForge;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests
{
    public class PathingFixture : IDisposable
    {
        public PathingFixture()
        {
            GridForgeLogger.Verbosity = GridForgeLogger.LogLevel.Error;
            GlobalGridManager.Setup();

            // Optional: preload some static config here if needed later
        }

        public void Dispose()
        {
            PathManager.UnloadAllCharts();
            PathManager.ClearAll();

            GlobalGridManager.Reset();
            TrailblazerManager.Reset();
        }
    }

    [CollectionDefinition("PathingCollection")]
    public class PathingCollection : ICollectionFixture<PathingFixture> { }
}
