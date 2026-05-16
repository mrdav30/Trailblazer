using FixedMathSharp;
using GridForge.Grids;
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

    public int RequestCacheKey { get; set; } = 1234;

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
        TrailblazerWorldContext? context = null)
    {
        return new FakeSurveyResult
        {
            _hasPath = hasPath,
            IsValid = hasPath,
            RequestHashKey = requestKey,
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
