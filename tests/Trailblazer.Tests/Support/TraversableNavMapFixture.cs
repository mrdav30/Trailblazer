using System;
using Xunit;
using Trailblazer.Navigation;
using GridForge.Grids;

namespace Trailblazer.Tests
{
    public class TraversableNavMapFixture : IDisposable
    {
        public TraversableNavMapFixture()
        {
            GridForgeLogger.Verbosity = GridForgeLogger.LogLevel.Error;
            GlobalGridManager.Setup();

            // Optional: preload some static config here if needed later
        }

        public void Dispose()
        {
            TraversableNavMapManager.UnloadAllMaps();
            TraversableNavMapManager.ClearAll();

            GlobalGridManager.Reset();
        }
    }

    [CollectionDefinition("TraversableNavMapCollection")]
    public class TraversableNavMapCollection : ICollectionFixture<TraversableNavMapFixture> { }
}
