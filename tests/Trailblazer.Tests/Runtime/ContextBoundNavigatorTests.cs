using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
using SwiftCollections;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;
using Trailblazer.Tests.Navigation;
using Xunit;

namespace Trailblazer.Tests.Worlds;

[Collection("PathingCollection")]
public sealed class ContextBoundNavigatorTests : IDisposable
{
    public void Dispose()
    {
        PathManager.Reset();
        TraversalTransitionRegistry.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Setup_WithContext_ShouldAssignDeterministicIdsPerContext()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();

        var contextAFirst = new TestNavigator(contextA);
        var contextASecond = new TestNavigator(contextA);
        var contextBFirst = new TestNavigator(contextB);
        var contextBSecond = new TestNavigator(contextB);

        contextAFirst.Setup(Vector3d.Zero);
        contextASecond.Setup(new Vector3d(1, 0, 0));
        contextBFirst.Setup(Vector3d.Zero);
        contextBSecond.Setup(new Vector3d(1, 0, 0));

        contextBFirst.GlobalId.Should().Be(contextAFirst.GlobalId);
        contextBSecond.GlobalId.Should().Be(contextASecond.GlobalId);
        contextAFirst.GlobalId.Should().NotBe(contextASecond.GlobalId);
    }

    [Fact]
    public void SetupAndActivate_ContextOverloads_ShouldBindBeforeInitializing()
    {
        using TrailblazerWorldContext context = CreateContextWithGrid();
        var setupNavigator = new TestNavigator();
        var activeNavigator = new TestNavigator();

        setupNavigator.Setup(context, Vector3d.Zero);
        activeNavigator.Activate(context, new TrekCondition(), new Vector3d(1, 0, 0));

        setupNavigator.Context.Should().BeSameAs(context);
        setupNavigator.IsActive.Should().BeFalse();
        setupNavigator.Position.Should().Be(Vector3d.Zero);
        activeNavigator.Context.Should().BeSameAs(context);
        activeNavigator.IsActive.Should().BeTrue();
    }

    [Fact]
    public void ContextReset_ShouldResetOnlyThatContextsNavigatorIdAllocator()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();

        var originalA = new TestNavigator(contextA);
        var originalB = new TestNavigator(contextB);
        originalA.Setup(Vector3d.Zero);
        originalB.Setup(Vector3d.Zero);

        Guid originalAId = originalA.GlobalId;
        Guid originalBId = originalB.GlobalId;

        contextA.Reset();

        var replayA = new TestNavigator(contextA);
        var nextB = new TestNavigator(contextB);
        replayA.Setup(Vector3d.Zero);
        nextB.Setup(new Vector3d(1, 0, 0));

        replayA.GlobalId.Should().Be(originalAId);
        nextB.GlobalId.Should().NotBe(originalBId);
    }

    [Fact]
    public void Setup_WithoutContext_ShouldThrow()
    {
        var navigator = new TestNavigator();

        navigator.Invoking(n => n.Setup(Vector3d.Zero))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*context*");
    }

    [Fact]
    public void Reset_ShouldDeregisterFromBoundContextWorld()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();

        var navigator = new TestNavigator(contextA);
        navigator.Setup(Vector3d.Zero);
        navigator.Initialize(new TrekCondition());

        ScanNavigatorCount(contextA, Vector3d.Zero).Should().Be(1);

        navigator.Reset();

        ScanNavigatorCount(contextA, Vector3d.Zero).Should().Be(0);
    }

    [Fact]
    public void Reset_ShouldReturnActiveGuideToOwningContext()
    {
        using TrailblazerWorldContext context = CreateContextWithGrid();
        PathTestFactory.RegisterSolidLine(context, "ContextNavigatorResetGuide", Vector3d.Zero, 3);

        var navigator = new TestNavigator(context);
        navigator.Setup(Vector3d.Zero, size: Fixed64.One);
        navigator.Initialize(new TrekCondition());
        navigator.ConfigureForGuidedTraversal(pathAlgorithm: SolidPathAlgorithm.AStar);
        navigator.ApplyGuidedTrekRequest(new Vector3d(2, 0, 0), rate: TrekRate.Moderate);

        AStarPathRequest request = navigator.Steering!.CurrentRequest.Should().BeOfType<AStarPathRequest>().Subject;
        context.Guides.RequestGuide(request, out AStarGuide? guide).Should().BeTrue();
        navigator.Steering.SetTrailGuide(guide);

        navigator.Steering.TrailGuide.Should().NotBeNull();
        context.Guides.InUseAStarGuideCount.Should().Be(1);

        navigator.Reset();

        context.Guides.InUseAStarGuideCount.Should().Be(0);
        navigator.Steering.TrailGuide.Should().BeNull();
        navigator.Steering.CurrentRequest.Should().BeNull();
    }

    [Fact]
    public void ApplyGuidedTrekRequest_ShouldCreateRequestBoundToNavigatorContext()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        PathTestFactory.RegisterSolidLine(contextA, "ContextA-GuidedNavigator", Vector3d.Zero, 3);

        var navigator = new TestNavigator(contextA);
        navigator.Setup(Vector3d.Zero, size: Fixed64.One);
        navigator.Initialize(new TrekCondition());

        navigator.ApplyGuidedTrekRequest(new Vector3d(2, 0, 0));

        navigator.Steering!.CurrentRequest.Should().NotBeNull();
        navigator.Steering!.CurrentRequest!.Context.Should().BeSameAs(contextA);
    }

    [Fact]
    public void MovementGroups_WithSameGroupId_ShouldStayContextLocal()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        var contextASession = new MovementGroupSession { GroupId = 7 };
        var contextBSession = new MovementGroupSession { GroupId = 7 };
        var contextBProbe = new MovementGroupSession { GroupId = 7 };
        Guid contextAOwner = Guid.NewGuid();
        Guid contextBOwner = Guid.NewGuid();
        Vector3d destination = new(5, 0, 0);

        contextA.Navigation.MovementGroups.Prewarm(
            contextASession,
            contextAOwner,
            destination,
            Vector3d.Zero,
            Fixed64.One);
        contextB.Navigation.MovementGroups.Prewarm(
            contextBSession,
            contextBOwner,
            destination,
            Vector3d.Zero,
            Fixed64.One);

        contextB.Navigation.MovementGroups.IsNeighbor(
                contextBProbe,
                contextAOwner,
                destination,
                contextB.FrameCount)
            .Should()
            .BeFalse();
        contextB.Navigation.MovementGroups.IsNeighbor(
                contextBProbe,
                contextBOwner,
                destination,
                contextB.FrameCount)
            .Should()
            .BeTrue();
    }

    private static TrailblazerWorldContext CreateContextWithGrid()
    {
        return PathTestFactory.CreateContextWithGrid(
            new Vector3d(-4, -4, -4),
            new Vector3d(16, 8, 8));
    }

    private static int ScanNavigatorCount(TrailblazerWorldContext context, Vector3d position)
    {
        var results = new SwiftList<ISteer>();
        var scratch = new GridScanScratch();
        GridScanManager.ScanRadiusInto<ISteer>(
            context.World,
            position,
            Fixed64.One,
            results,
            scratch);

        return results.Count;
    }
}
