using GridForge.Grids;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Tests;

internal static class TestWorld
{
    private static TrailblazerWorldContext? _context;

    public static TrailblazerWorldContext Context
    {
        get
        {
            if (_context == null || _context.IsDisposed || !_context.World.IsActive)
                Setup();

            return _context!;
        }
    }

    public static GridWorld World => Context.World;

    public static bool IsActive => _context != null && !_context.IsDisposed && _context.World.IsActive;

    public static void Setup(TrailblazerWorldContextSettings? settings = null)
    {
        Reset();
        _context = TrailblazerWorldContext.CreateOwned(settings: settings);
    }

    public static void Attach(GridWorld world, bool takeOwnership = false)
    {
        Reset();
        _context = TrailblazerWorldContext.Attach(world, takeOwnership);
    }

    public static void Reset()
    {
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
}
