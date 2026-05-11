using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using System;

namespace Trailblazer.Support;

/// <summary>
/// Owns Trailblazer's single active <see cref="GridWorld"/> and forwards grid lifecycle events
/// to callers that still expect a global grid entry point.
/// </summary>
/// <remarks>
/// This is a compatibility bridge for legacy static APIs while Trailblazer migrates to explicit
/// <see cref="Trailblazer.TrailblazerWorldContext"/> ownership. New multi-world integrations should
/// keep and pass their context handles directly instead of relying on this ambient active world.
/// </remarks>
public static class TrailblazerWorldManager
{
    private static readonly object Sync = new();

    private static GridWorld? _world;

    private static bool _ownsWorld;

    private static Action<GridEventInfo>? _onActiveGridAdded;

    private static Action<GridEventInfo>? _onActiveGridRemoved;

    private static Action<GridEventInfo>? _onActiveGridChange;

    private static Action? _onReset;

    /// <summary>
    /// Raised when the active world adds a grid.
    /// </summary>
    public static event Action<GridEventInfo> OnActiveGridAdded
    {
        add => _onActiveGridAdded += value;
        remove => _onActiveGridAdded -= value;
    }

    /// <summary>
    /// Raised when the active world removes a grid.
    /// </summary>
    public static event Action<GridEventInfo> OnActiveGridRemoved
    {
        add => _onActiveGridRemoved += value;
        remove => _onActiveGridRemoved -= value;
    }

    /// <summary>
    /// Raised when the active world reports a significant grid change.
    /// </summary>
    public static event Action<GridEventInfo> OnActiveGridChange
    {
        add => _onActiveGridChange += value;
        remove => _onActiveGridChange -= value;
    }

    /// <summary>
    /// Raised when the active world resets.
    /// </summary>
    public static event Action OnReset
    {
        add => _onReset += value;
        remove => _onReset -= value;
    }

    /// <summary>
    /// Gets whether Trailblazer currently has an active configured world.
    /// </summary>
    public static bool IsActive
    {
        get
        {
            lock (Sync)
                return _world?.IsActive == true;
        }
    }

    /// <summary>
    /// Gets the currently configured world.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when Trailblazer has not been attached to a <see cref="GridWorld"/>.
    /// </exception>
    public static GridWorld World
    {
        get
        {
            lock (Sync)
            {
                if (_world?.IsActive != true)
                {
                    throw new InvalidOperationException(
                        "Trailblazer requires an active GridWorld. Call TrailblazerManager.Initialize(world), " +
                        "or PathManager.Register(world, ...) first.");
                }

                return _world;
            }
        }
    }

    /// <summary>
    /// Gets the voxel size of the active world, or GridForge's default voxel size when none is configured.
    /// </summary>
    public static Fixed64 VoxelSize => IsActive ? World.VoxelSize : GridWorld.DefaultVoxelSize;

    /// <summary>
    /// Creates and configures a new owned world for tests or standalone pathing scenarios.
    /// </summary>
    public static void Setup(
        Fixed64? voxelSize = null,
        int spatialGridCellSize = GridWorld.DefaultSpatialGridCellSize)
    {
        AttachWorld(new GridWorld(voxelSize, spatialGridCellSize), takeOwnership: true);
    }

    /// <summary>
    /// Attaches Trailblazer to an explicit external world instance.
    /// </summary>
    /// <param name="world">The world to use for all Trailblazer grid access.</param>
    /// <param name="takeOwnership">
    /// True when this bridge should dispose the world during <see cref="Reset"/>; otherwise false.
    /// </param>
    public static void AttachWorld(GridWorld world, bool takeOwnership = false)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        GridWorld? previousWorld = null;
        bool disposePrevious = false;

        lock (Sync)
        {
            if (ReferenceEquals(_world, world))
            {
                _ownsWorld = _ownsWorld || takeOwnership;
                return;
            }

            previousWorld = _world;
            disposePrevious = _ownsWorld;
            DetachWorldHandlers_NoLock(previousWorld);

            _world = world;
            _ownsWorld = takeOwnership;
            AttachWorldHandlers_NoLock(world);
        }

        if (disposePrevious && previousWorld != null && previousWorld.IsActive)
            previousWorld.Dispose();
    }

    /// <summary>
    /// Resets and detaches the currently configured world.
    /// </summary>
    public static void Reset()
    {
        GridWorld? worldToReset;
        bool disposeWorld;

        lock (Sync)
        {
            worldToReset = _world;
            disposeWorld = _ownsWorld;
        }

        if (worldToReset == null)
            return;

        if (disposeWorld)
            worldToReset.Dispose();
        else if (worldToReset.IsActive)
            worldToReset.Reset();

        lock (Sync)
        {
            if (!ReferenceEquals(_world, worldToReset))
                return;

            DetachWorldHandlers_NoLock(worldToReset);
            _world = null;
            _ownsWorld = false;
        }
    }

    /// <inheritdoc cref="GridWorld.TryGetGrid(int, out VoxelGrid?)"/>
    public static bool TryGetGrid(int index, out VoxelGrid? grid)
    {
        grid = null;
        return IsActive && World.TryGetGrid(index, out grid);
    }

    /// <inheritdoc cref="GridWorld.TryGetGrid(Vector3d, out VoxelGrid?)"/>
    public static bool TryGetGrid(Vector3d position, out VoxelGrid? grid)
    {
        grid = null;
        return IsActive && World.TryGetGrid(position, out grid);
    }

    /// <inheritdoc cref="GridWorld.TryGetGrid(WorldVoxelIndex, out VoxelGrid?)"/>
    public static bool TryGetGrid(WorldVoxelIndex voxelIndex, out VoxelGrid? grid)
    {
        grid = null;
        return IsActive && World.TryGetGrid(voxelIndex, out grid);
    }

    /// <inheritdoc cref="GridWorld.TryGetGridAndVoxel(Vector3d,out VoxelGrid?,out Voxel?)"/>
    public static bool TryGetGridAndVoxel(
        Vector3d position,
        out VoxelGrid? grid,
        out Voxel? voxel)
    {
        grid = null;
        voxel = null;
        return IsActive && World.TryGetGridAndVoxel(position, out grid, out voxel);
    }

    /// <inheritdoc cref="GridWorld.TryGetGridAndVoxel(WorldVoxelIndex,out VoxelGrid?,out Voxel?)"/>
    public static bool TryGetGridAndVoxel(
        WorldVoxelIndex voxelIndex,
        out VoxelGrid? grid,
        out Voxel? voxel)
    {
        grid = null;
        voxel = null;
        return IsActive && World.TryGetGridAndVoxel(voxelIndex, out grid, out voxel);
    }

    /// <inheritdoc cref="GridWorld.TryGetVoxel(Vector3d, out Voxel?)"/>
    public static bool TryGetVoxel(Vector3d position, out Voxel? voxel)
    {
        voxel = null;
        return IsActive && World.TryGetVoxel(position, out voxel);
    }

    /// <inheritdoc cref="GridWorld.TryGetVoxel(WorldVoxelIndex, out Voxel?)"/>
    public static bool TryGetVoxel(WorldVoxelIndex voxelIndex, out Voxel? voxel)
    {
        voxel = null;
        return IsActive && World.TryGetVoxel(voxelIndex, out voxel);
    }

    /// <inheritdoc cref="GridWorld.TryAddGrid(GridConfiguration, out ushort)"/>
    public static bool TryAddGrid(GridConfiguration configuration, out ushort allocatedIndex)
    {
        allocatedIndex = ushort.MaxValue;
        return IsActive && World.TryAddGrid(configuration, out allocatedIndex);
    }

    /// <inheritdoc cref="GridWorld.TryRemoveGrid(ushort)"/>
    public static bool TryRemoveGrid(ushort gridIndex) =>
        IsActive && World.TryRemoveGrid(gridIndex);

    /// <inheritdoc cref="GridWorld.IncrementGridVersion(int, bool)"/>
    public static void IncrementGridVersion(int index, bool significant = false)
    {
        if (!IsActive)
            return;

        World.IncrementGridVersion(index, significant);
    }

    private static void AttachWorldHandlers_NoLock(GridWorld? world)
    {
        if (world == null)
            return;

        world.OnActiveGridAdded += HandleActiveGridAdded;
        world.OnActiveGridRemoved += HandleActiveGridRemoved;
        world.OnActiveGridChange += HandleActiveGridChange;
        world.OnReset += HandleReset;
    }

    private static void DetachWorldHandlers_NoLock(GridWorld? world)
    {
        if (world == null)
            return;

        world.OnActiveGridAdded -= HandleActiveGridAdded;
        world.OnActiveGridRemoved -= HandleActiveGridRemoved;
        world.OnActiveGridChange -= HandleActiveGridChange;
        world.OnReset -= HandleReset;
    }

    private static void HandleActiveGridAdded(GridEventInfo eventInfo) =>
        _onActiveGridAdded?.Invoke(eventInfo);

    private static void HandleActiveGridRemoved(GridEventInfo eventInfo) =>
        _onActiveGridRemoved?.Invoke(eventInfo);

    private static void HandleActiveGridChange(GridEventInfo eventInfo) =>
        _onActiveGridChange?.Invoke(eventInfo);

    private static void HandleReset() =>
        _onReset?.Invoke();
}
