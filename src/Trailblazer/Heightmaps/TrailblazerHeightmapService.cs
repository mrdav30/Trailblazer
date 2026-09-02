//=======================================================================
// TrailblazerHeightmapService.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Heightmaps;

/// <summary>
/// Context-owned API for registering and sampling deterministic heightmap layers.
/// </summary>
public sealed class TrailblazerHeightmapService
{
    private readonly TrailblazerWorldContext _context;

    internal TrailblazerHeightmapService(TrailblazerWorldContext context)
    {
        _context = context;
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
        return TrySampleGround(worldPosition, null, out sample);
    }

    /// <summary>
    /// Attempts to sample the preferred layer when valid, then falls back to deterministic candidate selection.
    /// </summary>
    public bool TrySampleGround(Vector3d worldPosition, string? preferredLayerName, out HeightmapSample sample)
    {
        EnsureUsable();

        if (!string.IsNullOrWhiteSpace(preferredLayerName)
            && State.LayersByName.TryGetValue(preferredLayerName, out HeightmapLayerRegistration preferredRegistration)
            && TrySampleRegistration(preferredRegistration, worldPosition, out Fixed64 preferredGroundY, out Fixed64 preferredDistance))
        {
            sample = CreateSample(preferredRegistration, worldPosition, preferredGroundY, preferredDistance);
            return true;
        }

        bool hasBest = false;
        HeightmapLayerRegistration? bestRegistration = null;
        Fixed64 bestGroundY = Fixed64.Zero;
        Fixed64 bestDistance = Fixed64.Zero;
        foreach (HeightmapLayerRegistration registration in State.LayersByName.Values)
        {
            if (!TrySampleRegistration(registration, worldPosition, out Fixed64 groundY, out Fixed64 distance))
                continue;

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

        sample = CreateSample(bestRegistration, worldPosition, bestGroundY, bestDistance);
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

    private static bool TrySampleRegistration(
        HeightmapLayerRegistration registration,
        Vector3d worldPosition,
        out Fixed64 groundY,
        out Fixed64 distance)
    {
        if (!registration.ContainsSelectionY(worldPosition.Y)
            || !registration.Surface.TrySampleGround(worldPosition, out groundY))
        {
            groundY = Fixed64.Zero;
            distance = Fixed64.Zero;
            return false;
        }

        distance = (worldPosition.Y - groundY).Abs();
        return true;
    }

    private static HeightmapSample CreateSample(
        HeightmapLayerRegistration registration,
        Vector3d worldPosition,
        Fixed64 groundY,
        Fixed64 distance)
    {
        return new HeightmapSample(
            registration.LayerName,
            worldPosition,
            groundY,
            distance);
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
