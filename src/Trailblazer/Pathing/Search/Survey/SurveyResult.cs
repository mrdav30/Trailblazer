using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Represents the base class for survey result data, providing common state and behavior for survey result implementations.
/// </summary>
/// <remarks>
/// This abstract class defines shared properties and methods used to manage the lifecycle and
/// state of survey results, such as validity, usage tracking, and chart utilization. 
/// Derived classes should implement additional functionality specific to their survey result type. 
/// Thread safety is not guaranteed; callers should ensure appropriate synchronization if instances are accessed concurrently.
/// </remarks>
public abstract class SurveyResult : ISurveyResult
{
    private int _activeCheckoutCount;

    internal TrailblazerWorldContext? Context { get; set; }

    /// <inheritdoc/>
    public bool IsValid { get; protected set; }

    /// <inheritdoc/>
    public bool IsInUse => _activeCheckoutCount > 0;

    /// <summary>
    /// Gets the number of active guide leases that reference this result.
    /// </summary>
    internal int ActiveCheckoutCount => _activeCheckoutCount;

    /// <inheritdoc/>
    public string[] ChartsUtilized { get; protected set; } = Array.Empty<string>();

    /// <inheritdoc/>
    public int LastUsedFrame { get; protected set; }

    /// <inheritdoc/>
    public PathRequestCacheKey RequestCacheKey { get; protected set; }

    /// <inheritdoc/>
    public virtual bool HasPath => false;

    /// <inheritdoc/>
    public void Checkout() => _activeCheckoutCount++;

    /// <inheritdoc/>
    public void Release()
    {
        if (_activeCheckoutCount == 0)
            return;

        _activeCheckoutCount--;
        if (_activeCheckoutCount == 0)
            LastUsedFrame = Context?.FrameCount ?? -1;
    }

    /// <summary>
    /// Releases every active lease when the owning cache invalidates or replaces this result.
    /// </summary>
    internal void ReleaseAllCheckouts()
    {
        if (_activeCheckoutCount == 0)
            return;

        _activeCheckoutCount = 0;
        LastUsedFrame = Context?.FrameCount ?? -1;
    }

    /// <inheritdoc/>
    public virtual void Reset()
    {
        IsValid = false;
        _activeCheckoutCount = 0;
        Context = null;
        ChartsUtilized = Array.Empty<string>();
        LastUsedFrame = -1;
        RequestCacheKey = default;
    }
}
