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
    internal TrailblazerWorldContext? Context { get; set; }

    /// <inheritdoc/>
    public bool IsValid { get; protected set; }

    /// <inheritdoc/>
    public bool IsInUse { get; protected set; }

    /// <inheritdoc/>
    public string[] ChartsUtilized { get; protected set; } = Array.Empty<string>();

    /// <inheritdoc/>
    public int LastUsedFrame { get; protected set; }

    /// <inheritdoc/>
    public int RequestHashKey { get; protected set; }

    /// <inheritdoc/>
    public virtual bool HasPath => false;

    /// <inheritdoc/>
    public void Checkout() => IsInUse = true;

    /// <inheritdoc/>
    public void Release()
    {
        IsInUse = false;
        LastUsedFrame = Context?.FrameCount ?? -1;
    }

    /// <inheritdoc/>
    public virtual void Reset()
    {
        IsValid = false;
        IsInUse = false;
        Context = null;
        ChartsUtilized = Array.Empty<string>();
        LastUsedFrame = -1;
        RequestHashKey = -1;
    }
}
