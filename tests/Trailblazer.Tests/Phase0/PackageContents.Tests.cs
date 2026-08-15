using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Trailblazer.Tests.Phase0;

public sealed class PackageContentsTests
{
    [Fact]
    public void BuiltPackage_ShouldPairTrailblazerAssemblyAndXmlDocumentationForEveryTarget()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
            && !File.Exists(Path.Combine(directory.FullName, "src", "Trailblazer", "Trailblazer.csproj")))
        {
            directory = directory.Parent;
        }
        directory.Should().NotBeNull("the test must run beneath the Trailblazer repository");
        string repositoryRoot = directory!.FullName;
        string configuration = BuildConfiguration;
        string packageId = IsLean ? "Trailblazer.Lean" : "Trailblazer";
        string packageVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "TrailblazerPackageVersion")
            .Value!;
        string packagePath = Path.Combine(
            repositoryRoot,
            "src",
            "Trailblazer",
            "bin",
            configuration,
            $"{packageId}.{packageVersion}.nupkg");

        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string[] entries = package.Entries.Select(entry => entry.FullName).ToArray();
        entries.Should().Contain(new[]
        {
            "lib/net8.0/Trailblazer.dll",
            "lib/net8.0/Trailblazer.xml",
            "lib/netstandard2.1/Trailblazer.dll",
            "lib/netstandard2.1/Trailblazer.xml"
        });
        entries.Should().NotContain(entry => entry.EndsWith("/GridForge.xml", StringComparison.Ordinal));
    }

#if DEBUG
    private const string BuildConfiguration = "Debug";
#elif TRAILBLAZER_DISABLE_MEMORYPACK
    private const string BuildConfiguration = "ReleaseLean";
#else
    private const string BuildConfiguration = "Release";
#endif

#if TRAILBLAZER_DISABLE_MEMORYPACK
    private const bool IsLean = true;
#else
    private const bool IsLean = false;
#endif
}
