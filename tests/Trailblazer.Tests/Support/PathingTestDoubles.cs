using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Tests;

internal sealed class TestPathRequest : IPathRequest
{
    private readonly TrailblazerWorldContext _context;

    public TestPathRequest(TrailblazerWorldContext? context = null)
    {
        _context = context ?? TestWorld.Context;
    }

    public TestPathRequest(int requestCacheKey, TrailblazerWorldContext? context = null)
        : this(CreateCacheKey(requestCacheKey), context)
    {
    }

    public TestPathRequest(PathRequestCacheKey requestCacheKey, TrailblazerWorldContext? context = null)
        : this(context)
    {
        RequestCacheKey = requestCacheKey;
        IsValid = true;
    }

    public TrailblazerWorldContext Context => _context;

    public Vector3d Origin { get; set; }

    public Voxel? StartNode { get; set; }

    public Vector3d TargetPosition { get; set; }

    public Voxel? EndNode { get; set; }

    public Fixed64 UnitSize { get; set; } = Fixed64.One;

    public bool HasZeroDisplacement => StartNode == EndNode;

    public bool AllowUnwalkableEndpoints { get; set; }

    public int MaxPathSearchRange { get; set; }

    public bool HasOrigin => StartNode != null;

    public bool HasDestination => EndNode != null;

    public bool HasValidEndpoints => HasOrigin && HasDestination;

    public bool IsValid { get; set; }

    public PathRequestCacheKey RequestCacheKey { get; set; } = CreateCacheKey(1234);

    internal static PathRequestCacheKey CreateCacheKey(int identity)
    {
        WorldVoxelIndex origin = new(1, 0, 1, new VoxelIndex(identity, 0, 0));
        WorldVoxelIndex destination = new(1, 0, 1, new VoxelIndex(identity, 0, 1));
        return PathRequestCacheKey.CreateAStar(
            origin,
            destination,
            Fixed64.One,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            HeuristicMethod.Manhattan,
            Fixed64.One,
            maxPathSearchRange: 1,
            transitionRegistryVersion: 0);
    }

    public bool UpdateRequest(Vector3d origin, Vector3d destination, Fixed64? unitSize) => false;

    public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false) => false;

    public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false) => false;

    public bool TrySetUnitSize(Fixed64 unitSize) => false;
}

internal sealed class FakeSurveyResult : SurveyResult
{
    private bool _hasPath = true;

    public int ResetCount { get; private set; }

    public override bool HasPath => IsValid && _hasPath;

    public static FakeSurveyResult Create(
        int requestKey,
        bool hasPath = true,
        string[]? chartsUtilized = null,
        TrailblazerWorldContext? context = null) =>
        Create(TestPathRequest.CreateCacheKey(requestKey), hasPath, chartsUtilized, context);

    public static FakeSurveyResult Create(
        PathRequestCacheKey requestKey,
        bool hasPath = true,
        string[]? chartsUtilized = null,
        TrailblazerWorldContext? context = null)
    {
        return new FakeSurveyResult
        {
            _hasPath = hasPath,
            IsValid = hasPath,
            RequestCacheKey = requestKey,
            Context = context ?? TestWorld.Context,
            LastUsedFrame = -1,
            ChartsUtilized = chartsUtilized ?? System.Array.Empty<string>()
        };
    }

    public override void Reset()
    {
        ResetCount++;
        base.Reset();
    }
}

internal sealed class FakeGuide : IGuide
{
    public void Reset()
    {
    }

    public bool TryGetMovementDirection(Vector3d origin, out Vector3d direction)
    {
        direction = Vector3d.Zero;
        return false;
    }

    public bool TryGetFallbackDirection(Vector3d from, out Vector3d fallbackDirection)
    {
        fallbackDirection = Vector3d.Zero;
        return false;
    }
}
