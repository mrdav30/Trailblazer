//=======================================================================
// TrailblazerClock.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Runtime.CompilerServices;
using Chronicler.Timing;
using FixedMathSharp;
using FixedMathSharp.Chronicler;

namespace Trailblazer;

/// <summary>
/// Stores deterministic fixed-step timing state for one Trailblazer runtime owner.
/// </summary>
internal sealed class TrailblazerClock
{
    internal const int DefaultFrameRate = 32;

    private int _frameRate = DefaultFrameRate;

    private Fixed64 _deltaTime = Fixed64.One / (Fixed64)DefaultFrameRate;

    private readonly ChronicleClock _timeline = new(FixedChronicleTime.FromFixed64(
        Fixed64.One / (Fixed64)DefaultFrameRate));

    private Fixed64 _invDeltaTime = (Fixed64)DefaultFrameRate;

    private object _lifetime = new();

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
    public Fixed64 InvDeltaTime => _invDeltaTime;

    /// <summary>
    /// Gets the number of simulated frames.
    /// </summary>
    public long FrameCount => _timeline.FrameCount;

    internal object Lifetime => _lifetime;

    /// <summary>
    /// Gets the total simulated time in seconds.
    /// </summary>
    public ChronicleTimestamp ElapsedTime => _timeline.ElapsedTime;

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
        _timeline.Advance();
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
        _lifetime = new object();
        _timeline.Reset();
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
        ChronicleGuard.ThrowIfArgumentOutOfRange(
            frameRate <= 0,
            actualValue: frameRate,
            paramName: nameof(frameRate),
            message: "Frame rate must be greater than zero.");

        _frameRate = frameRate;
        _deltaTime = Fixed64.One / (Fixed64)_frameRate;
        _invDeltaTime = Fixed64.One / _deltaTime;
        _timeline.SetStepDuration(FixedChronicleTime.FromFixed64(_deltaTime));
    }

    /// <summary>
    /// Counts complete steps at the current step size, not historical frame indices.
    /// </summary>
    /// <param name="duration">The nonnegative duration in seconds.</param>
    /// <returns>The number of complete steps in the duration.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long GetFrameCountForDuration(Fixed64 duration)
    {
        return FixedChronicleTime.GetFrameCountForDuration(duration, _deltaTime);
    }
}
