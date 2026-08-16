//=======================================================================
// TrailblazerGuideState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Owns guide result caches and reusable guide pools for one pathing context.
/// </summary>
internal sealed class TrailblazerGuideState : IDisposable
{
    private bool _disposed;

    internal VolumeSurveyor VolumeSurveyor { get; } = new();

    internal ReusableSurveyResultCache<VolumeSurveyResult> CachedVolumeResults { get; } = new();

    internal GuidePool<VolumeGuide> VolumeGuides { get; } =
        new(static () => new VolumeGuide(), static guide => guide.ResetForReuse());

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CachedVolumeResults.Dispose();
    }
}
