//=======================================================================
// TrailblazerClock.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;

namespace Trailblazer;

/// <summary>
/// Stores deterministic fixed-step timing state for one Trailblazer runtime owner.
/// </summary>
internal sealed class TrailblazerClock
{
    internal const int DefaultFrameRate = 32;

    private int _frameRate = DefaultFrameRate;

    private Fixed64 _deltaTime = Fixed64.One / (Fixed64)DefaultFrameRate;

    /// <summary>
    /// Gets the fixed simulation frame rate.
    /// </summary>
    public int FrameRate => _frameRate;

    /// <summary>
    /// Gets the fixed time step for each simulation frame.
    /// </summary>
    public Fixed64 DeltaTime => _deltaTime;

    /// <summary>
    /// Gets the reciprocal of the current fixed time step.
    /// </summary>
    public Fixed64 InvDeltaTime => Fixed64.One / _deltaTime;

    /// <summary>
    /// Gets the number of simulated frames.
    /// </summary>
    public int FrameCount { get; private set; }

    /// <summary>
    /// Gets the total simulated time in seconds.
    /// </summary>
    public Fixed64 TotalTime { get; private set; }

    /// <summary>
    /// Gets the accumulated visualization time since the last late-simulate reset.
    /// </summary>
    public Fixed64 AccumulatedTime { get; private set; }

    /// <summary>
    /// Gets whether the next visualization step should reset accumulated time.
    /// </summary>
    public bool ResetAccumulation { get; private set; }

    /// <summary>
    /// Gets the accumulated visualization time expressed in simulation frames.
    /// </summary>
    public Fixed64 ExpectedAccumulation { get; private set; }

    /// <summary>
    /// Advances the fixed simulation frame.
    /// </summary>
    public void Simulate()
    {
        FrameCount++;
        TotalTime += _deltaTime;
    }

    /// <summary>
    /// Marks visualization accumulation for reset after a fixed simulation frame completes.
    /// </summary>
    public void LateSimulate()
    {
        ResetAccumulation = true;
    }

    /// <summary>
    /// Advances deterministic visualization accumulation by one fixed step.
    /// </summary>
    public void Visualize()
    {
        if (ResetAccumulation)
        {
            AccumulatedTime = Fixed64.Zero;
            ResetAccumulation = false;
        }

        AccumulatedTime += _deltaTime;
        ExpectedAccumulation = AccumulatedTime / _deltaTime;
    }

    /// <summary>
    /// Resets elapsed timing state while preserving the configured frame rate.
    /// </summary>
    public void Reset()
    {
        FrameCount = 0;
        TotalTime = Fixed64.Zero;
        AccumulatedTime = Fixed64.Zero;
        ExpectedAccumulation = Fixed64.Zero;
        ResetAccumulation = false;
    }

    /// <summary>
    /// Updates the fixed simulation frame rate.
    /// </summary>
    /// <param name="frameRate">The new frame rate. Must be greater than zero.</param>
    public void SetFrameRate(int frameRate)
    {
        if (frameRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameRate),
                frameRate,
                "Frame rate must be greater than zero.");
        }

        _frameRate = frameRate;
        _deltaTime = Fixed64.One / (Fixed64)_frameRate;
    }

    /// <summary>
    /// Calculates the frame index containing the specified fixed-point timestamp.
    /// </summary>
    /// <param name="timestamp">The timestamp to resolve.</param>
    /// <returns>The zero-based frame index for the timestamp.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetFrameFromTime(Fixed64 timestamp)
    {
        return (timestamp * InvDeltaTime).FloorToInt();
    }
}
