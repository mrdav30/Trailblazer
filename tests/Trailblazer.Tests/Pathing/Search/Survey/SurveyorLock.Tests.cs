using System;
using System.Reflection;
using System.Threading.Tasks;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class SurveyorLockTests : IDisposable
{
    public SurveyorLockTests()
    {
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-1, -1, -1), new Vector3d(12, 4, 12));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Surveyors_ShouldUseIndependentScratchLocks_InsteadOfGlobalLock()
    {
        typeof(SurveyorLock)
            .GetField("GlobalLock", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Should().BeNull();

        object? flowFieldLock = GetScratchLock(FlowFieldSurveyor.Shared);
        object? volumeLock = GetScratchLock(VolumeSurveyor.Shared);

        flowFieldLock?.GetType().Name.Should().Be(nameof(SurveyorLock));
        volumeLock?.GetType().Name.Should().Be(nameof(SurveyorLock));
        flowFieldLock.Should().NotBeSameAs(volumeLock);
    }

    [Fact]
    public async Task SharedSurveyors_ShouldResolveMixedConcurrentRequests_WithoutStateLeak()
    {
        NavigationChartCell[,,] data = new NavigationChartCell[1, 8, 8];
        for (int x = 0; x < 8; x++)
            for (int z = 0; z < 8; z++)
                data[0, x, z] = NavigationChartCell.SolidGas;

        NavigationChart chart = NavigationChart.From3D("ConcurrentSurveyors", data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(chart);

        FlowFieldPathRequest flowFieldRequest = TestRequire.Created(
            FlowFieldPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(7, 0, 7), out FlowFieldPathRequest? createdFlowField),
            createdFlowField);
        VolumePathRequest volumeRequest = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(7, 0, 7),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        Task<FlowFieldSurveyResult[]> flowFieldTask = Task.Run(() => RunFlowField(flowFieldRequest));
        Task<VolumeSurveyResult[]> volumeTask = Task.Run(() => RunVolume(volumeRequest));

        FlowFieldSurveyResult[] flowFieldResults = await flowFieldTask;
        VolumeSurveyResult[] volumeResults = await volumeTask;

        flowFieldResults.Should().AllSatisfy(result =>
        {
            result.HasPath.Should().BeTrue();
            TestRequire.NotNull(result.Fields).Count.Should().Be(64);
        });
        volumeResults.Should().AllSatisfy(result =>
        {
            result.HasPath.Should().BeTrue();
            AStarWaypoint[] waypoints = TestRequire.NotNull(result.Waypoints);
            waypoints[0].Position.Should().Be(Vector3d.Zero);
            waypoints[waypoints.Length - 1].Position.Should().Be(new Vector3d(7, 0, 7));
        });

        TestWorld.Context.Pathing.UnloadChart("ConcurrentSurveyors");
    }

    private static object? GetScratchLock(object surveyor)
    {
        return surveyor.GetType()
            .GetField("_scratchLock", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(surveyor);
    }

    private static FlowFieldSurveyResult[] RunFlowField(FlowFieldPathRequest request)
    {
        var results = new FlowFieldSurveyResult[8];
        for (int i = 0; i < results.Length; i++)
            results[i] = FlowFieldSurveyor.Shared.FindPath(request);

        return results;
    }

    private static VolumeSurveyResult[] RunVolume(VolumePathRequest request)
    {
        var results = new VolumeSurveyResult[8];
        for (int i = 0; i < results.Length; i++)
            results[i] = VolumeSurveyor.Shared.FindPath(request);

        return results;
    }
}
