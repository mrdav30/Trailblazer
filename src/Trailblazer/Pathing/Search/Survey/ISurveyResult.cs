//=======================================================================
// ISurveyResult.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
    /// Indicates whether one or more active guides currently reference the result.
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
    /// The exact cache identity of the request that generated this result.
    /// </summary>
    PathRequestCacheKey RequestCacheKey { get; }

    /// <summary>
    /// Adds an active guide lease for the result.
    /// </summary>
    void Checkout();

    /// <summary>
    /// Releases one active guide lease and makes the result reusable after the final lease is returned.
    /// </summary>
    void Release();

    /// <summary>
    /// Resets the object to its initial state.
    /// </summary>
    void Reset();
}
