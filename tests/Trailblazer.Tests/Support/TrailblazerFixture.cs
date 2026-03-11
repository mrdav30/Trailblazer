using System;
using Xunit;

namespace Trailblazer.Tests;

/// <summary>
/// Class Fixture for all GridForge tests, ensuring proper setup and teardown.
/// </summary>
public class TrailblazerFixture : IDisposable
{
    public void Dispose()
    {
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }
}

[CollectionDefinition("TrailblazerCollection")]
public class TrailblazerCollection : ICollectionFixture<TrailblazerFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
