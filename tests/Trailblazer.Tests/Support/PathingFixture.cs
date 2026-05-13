using GridForge;
using SwiftCollections.Diagnostics;
using System;
using Xunit;

namespace Trailblazer.Tests;

public class PathingFixture : IDisposable
{
    public PathingFixture()
    {
        GridForgeLogger.MinimumLevel = DiagnosticLevel.Error;
        TrailblazerLogger.MinimumLevel = DiagnosticLevel.Error;
        TrailblazerLogger.EnableDebugLogging = false;
        TestWorld.Setup();
    }

    public void Dispose()
    {
        TestWorld.Reset();

        GC.SuppressFinalize(this);
    }
}

[CollectionDefinition("PathingCollection")]
public class PathingCollection : ICollectionFixture<PathingFixture> { }
