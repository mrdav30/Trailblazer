namespace Trailblazer.Pathing;

public interface ISurveyResult
{
    bool IsValid { get; }

    /// <summary>
    /// Indicates whether the result has a valid path and can be used.
    /// </summary>
    bool HasPath { get; }

    /// <summary>
    /// Indicates whether the result is currently in use by an agent.
    /// </summary>
    bool IsInUse { get; }

    string[] ChartsUtilized { get; }

    /// <summary>
    /// The frame in which this result was last used, used for eviction or reuse logic.
    /// </summary>
    int LastUsedFrame { get; }

    /// <summary>
    /// A unique hash key representing the request that generated this result.
    /// </summary>
    int RequestHashKey { get; }

    /// <summary>
    /// Marks the result as in use for the current frame or request.
    /// </summary>
    void Checkout();

    /// <summary>
    /// Releases the result for reuse or reinitialization.
    /// </summary>
    void Release();

    void Reset();
}
