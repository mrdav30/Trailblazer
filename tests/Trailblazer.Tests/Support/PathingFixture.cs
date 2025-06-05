using System;
using Xunit;
using GridForge.Grids;
using Trailblazer.Pathing;

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
            PathManager.UnloadAllMaps();
            PathManager.ClearAll();

            GlobalGridManager.Reset();
        }
    }

    [CollectionDefinition("PathingCollection")]
    public class PathingCollection : ICollectionFixture<PathingFixture> { }
}
