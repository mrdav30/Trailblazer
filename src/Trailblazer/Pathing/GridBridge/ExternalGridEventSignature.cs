using FixedMathSharp;
using GridForge.Configuration;
using System;

namespace Trailblazer.Pathing;

internal readonly struct ExternalGridEventSignature : IEquatable<ExternalGridEventSignature>
{
    public ExternalGridEventSignature(
        ExternalGridEventKind eventKind,
        long gridSpawnToken,
        uint gridVersion,
        GridConfiguration configuration,
        Vector3d boundsMin,
        Vector3d boundsMax)
    {
        EventKind = eventKind;
        GridSpawnToken = gridSpawnToken;
        GridVersion = gridVersion;
        Configuration = configuration;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }

    public ExternalGridEventKind EventKind { get; }

    public long GridSpawnToken { get; }

    public uint GridVersion { get; }

    public GridConfiguration Configuration { get; }

    public Vector3d BoundsMin { get; }

    public Vector3d BoundsMax { get; }

    public bool Equals(ExternalGridEventSignature other)
    {
        return EventKind == other.EventKind
            && GridSpawnToken == other.GridSpawnToken
            && GridVersion == other.GridVersion
            && Configuration.Equals(other.Configuration)
            && BoundsMin == other.BoundsMin
            && BoundsMax == other.BoundsMax;
    }
}
