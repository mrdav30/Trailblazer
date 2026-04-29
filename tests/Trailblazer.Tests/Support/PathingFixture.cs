using GridForge;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests;

public class PathingFixture : IDisposable
{
    public PathingFixture()
    {
        GridForgeLogger.MinimumLevel = SwiftCollections.Diagnostics.DiagnosticLevel.Error;
        TrailblazerWorldManager.Setup();
    }

    public void Dispose()
    {
        PathManager.Reset();

        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();

        GC.SuppressFinalize(this);
    }
}

[CollectionDefinition("PathingCollection")]
public class PathingCollection : ICollectionFixture<PathingFixture> { }
