using GridForge;
using SwiftCollections.Diagnostics;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests;

public class PathingFixture : IDisposable
{
    public PathingFixture()
    {
        GridForgeLogger.MinimumLevel = DiagnosticLevel.Error;
        TrailblazerLogger.MinimumLevel = DiagnosticLevel.Error;
        TrailblazerLogger.EnableDebugLogging = false;
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
