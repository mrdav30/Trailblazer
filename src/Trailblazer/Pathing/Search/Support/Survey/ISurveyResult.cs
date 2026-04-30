namespace Trailblazer.Pathing;

/// <summary>
/// Represents the result of a survey operation, providing access to its state, usage information, and lifecycle management methods.
/// </summary>
/// <remarks>
/// Implementations of this interface expose properties to determine the validity, usage status, and associated metadata of a survey result. 
/// Methods are provided to manage the lifecycle of the result, including marking it as in use, releasing it for reuse, and resetting its state.
/// </remarks>
public interface ISurveyResult
{
    /// <summary>
    /// Gets a value indicating whether the current object is in a valid state.
    /// </summary>
    bool IsValid { get; }

    /// <summary>
    /// Indicates whether the result has a valid path and can be used.
    /// </summary>
    bool HasPath { get; }

    /// <summary>
    /// Indicates whether the result is currently in use by an agent.
    /// </summary>
    bool IsInUse { get; }

    /// <summary>
    /// Gets the names of the charts that are utilized by the current instance.
    /// </summary>
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

    /// <summary>
    /// Resets the object to its initial state.
    /// </summary>
    void Reset();
}
