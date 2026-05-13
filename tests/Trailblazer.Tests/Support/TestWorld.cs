using FixedMathSharp;
using GridForge.Grids;
using System;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;

namespace Trailblazer.Tests;

internal static class TestWorld
{
    private static TrailblazerWorldContext? _context;

    [ThreadStatic]
    private static IDisposable? _pathingScope;

    public static TrailblazerWorldContext Context
    {
        get
        {
            if (_context == null || _context.IsDisposed || !_context.World.IsActive)
                Setup();

            EnsurePathingState();
            return _context!;
        }
    }

    public static GridWorld World => Context.World;

    public static Fixed64 VoxelSize => Context.VoxelSize;

    public static bool IsActive => _context != null && !_context.IsDisposed && _context.World.IsActive;

    public static void Setup()
    {
        Reset();
        _context = TrailblazerWorldContext.CreateOwned();
        EnsurePathingState();
    }

    public static void Attach(GridWorld world, bool takeOwnership = false)
    {
        Reset();
        _context = TrailblazerWorldContext.Attach(world, takeOwnership);
        EnsurePathingState();
    }

    public static void Reset()
    {
        _pathingScope?.Dispose();
        _pathingScope = null;
        _context?.Dispose();
        _context = null;
    }

    public static LocomotionHandler Bind(LocomotionHandler handler)
    {
        handler.BindContext(Context);
        return handler;
    }

    public static JumpLocomotion Bind(JumpLocomotion locomotion)
    {
        locomotion.BindContext(Context);
        return locomotion;
    }

    public static PlatformLocomotion Bind(PlatformLocomotion locomotion)
    {
        locomotion.BindContext(Context);
        return locomotion;
    }

    public static WaterLocomotion Bind(WaterLocomotion locomotion)
    {
        locomotion.BindContext(Context);
        return locomotion;
    }

    private static void EnsurePathingState()
    {
        if (_context == null)
            return;

        if (PathManager.TryGetActiveState(out PathingWorldState? state)
            && ReferenceEquals(state, _context.Pathing.State))
            return;

        _pathingScope?.Dispose();
        _pathingScope = PathManager.EnterState(_context.Pathing.State);
    }
}
