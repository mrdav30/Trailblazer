using System;
using FixedMathSharp;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks;

/// <summary>A headless Navigator with horizontal ground support and no collision solver.</summary>
internal sealed class BenchmarkNavigator : Navigator
{
    private readonly TrailblazerWorldContext _worldContext;
    private readonly Vector3d _initialRoot;
    private readonly NavigationAgentProfile _profile;
    private readonly Guid _identity;

    internal BenchmarkNavigator(
        TrailblazerWorldContext context,
        Vector3d foot,
        NavigationAgentProfile profile,
        int index)
    {
        _worldContext = context;
        _initialRoot = foot + Vector3d.Up * profile.Shape.RootToFootOffsetY;
        _profile = profile;
        _identity = new Guid(index + 1, 0, 0, new byte[8]);
    }

    // Reset/setup are deliberately outside timing: every measurement starts with
    // the same pose, motor state, input, and occupancy instead of drifting off-map.
    internal void PrepareFrame()
    {
        Reset();
        Setup(_worldContext, _initialRoot, _profile, globalId: _identity);
        Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = _initialRoot.Y - _profile.Shape.RootToFootOffsetY,
            GroundState = new GroundCondition { SurfaceNormal = Vector3d.Up }
        });
    }

    public override void CheckTrekCondition()
    {
        _position.Y = _initialRoot.Y;
        SetGroundContact(FootPosition.Y, Vector3d.Up);
    }
}
