using FixedMathSharp;
using SwiftCollections;
using System;

namespace Trailblazer.Heightmaps;

/// <summary>
/// Context-owned API for registering and sampling deterministic heightmap layers.
/// </summary>
public sealed class TrailblazerHeightmapService
{
    private readonly TrailblazerWorldContext _context;

    internal TrailblazerHeightmapService(TrailblazerWorldContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        State = new HeightmapWorldState();
    }

    internal HeightmapWorldState State { get; }

    /// <summary>
    /// Registers a heightmap layer for this context.
    /// </summary>
    public bool Register(
        HeightmapSurface surface,
        Fixed64 minSelectionY,
        Fixed64 maxSelectionY,
        int priority = 0)
    {
        EnsureUsable();
        if (surface == null)
            throw new ArgumentNullException(nameof(surface));
        if (maxSelectionY <= minSelectionY)
            throw new ArgumentOutOfRangeException(nameof(maxSelectionY), "Maximum selection Y must be greater than minimum selection Y.");
        if (State.LayersByName.ContainsKey(surface.Name))
            return false;

        var registration = new HeightmapLayerRegistration(
            surface,
            minSelectionY,
            maxSelectionY,
            priority,
            State.NextRegistrationOrder++);
        State.LayersByName[surface.Name] = registration;
        return true;
    }

    /// <summary>
    /// Unregisters a heightmap layer by name.
    /// </summary>
    public bool Unregister(string layerName)
    {
        EnsureUsable();
        if (string.IsNullOrWhiteSpace(layerName))
            return false;

        return State.LayersByName.Remove(layerName);
    }

    /// <summary>
    /// Returns true when a layer with the supplied name is registered.
    /// </summary>
    public bool IsRegistered(string layerName)
    {
        EnsureUsable();
        return !string.IsNullOrWhiteSpace(layerName) && State.LayersByName.ContainsKey(layerName);
    }

    /// <summary>
    /// Attempts to retrieve context-local registration metadata for a layer.
    /// </summary>
    public bool TryGetRegistration(string layerName, out HeightmapLayerRegistration registration)
    {
        EnsureUsable();
        if (!string.IsNullOrWhiteSpace(layerName)
            && State.LayersByName.TryGetValue(layerName, out HeightmapLayerRegistration found))
        {
            registration = found;
            return true;
        }

        registration = null!;
        return false;
    }

    /// <summary>
    /// Attempts to sample the best deterministic registered layer that contains the query X/Z and contact Y.
    /// </summary>
    public bool TrySampleGround(Vector3d worldPosition, out HeightmapSample sample)
    {
        EnsureUsable();

        bool hasBest = false;
        HeightmapLayerRegistration? bestRegistration = null;
        Fixed64 bestGroundY = Fixed64.Zero;
        Fixed64 bestDistance = Fixed64.Zero;
        foreach (HeightmapLayerRegistration registration in State.LayersByName.Values)
        {
            if (!registration.ContainsSelectionY(worldPosition.y))
                continue;

            if (!registration.Surface.TrySampleGround(worldPosition, out Fixed64 groundY))
                continue;

            Fixed64 distance = (worldPosition.y - groundY).Abs();
            if (hasBest && !IsBetterCandidate(registration, distance, bestRegistration!, bestDistance))
                continue;

            hasBest = true;
            bestRegistration = registration;
            bestGroundY = groundY;
            bestDistance = distance;
        }

        if (!hasBest || bestRegistration == null)
        {
            sample = default;
            return false;
        }

        sample = new HeightmapSample(
            bestRegistration.LayerName,
            worldPosition,
            bestGroundY,
            bestDistance);
        return true;
    }

    /// <summary>
    /// Clears all heightmap layers registered to this context.
    /// </summary>
    public void Reset()
    {
        EnsureUsable();
        State.Reset();
    }

    internal void Dispose()
    {
        State.Reset();
    }

    private static bool IsBetterCandidate(
        HeightmapLayerRegistration candidate,
        Fixed64 candidateDistance,
        HeightmapLayerRegistration current,
        Fixed64 currentDistance)
    {
        if (candidateDistance != currentDistance)
            return candidateDistance < currentDistance;

        if (candidate.Priority != current.Priority)
            return candidate.Priority > current.Priority;

        return candidate.RegistrationOrder < current.RegistrationOrder;
    }

    private void EnsureUsable()
    {
        SwiftThrowHelper.ThrowIfDisposed(_context.IsDisposed, nameof(TrailblazerWorldContext));
        if (!_context.World.IsActive)
            throw new InvalidOperationException("TrailblazerHeightmapService is bound to an inactive GridWorld.");
    }
}
