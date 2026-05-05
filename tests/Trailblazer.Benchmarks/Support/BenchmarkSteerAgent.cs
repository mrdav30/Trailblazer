using FixedMathSharp;
using System;
using Trailblazer.Navigation;

namespace Trailblazer.Benchmarks;

/// <summary>
/// A minimal benchmark-owned <see cref="ISteer"/> implementation.
/// This avoids any dependency on the test project's MockSteerAgent and keeps
/// benchmark assemblies self-contained.
/// </summary>
internal sealed class BenchmarkSteerAgent : ISteer
{
    public Guid GlobalId { get; } = Guid.NewGuid();

    public Vector3d Position { get; set; }

    public Vector3d Velocity { get; set; } = Vector3d.Zero;

    public Fixed64 Speed { get; set; } = Fixed64.Zero;

    public Vector3d Acceleration { get; set; } = Vector3d.Zero;

    /// <summary>
    /// Minimum speed before the agent is considered stuck.
    /// Matches the test project default of 10 to keep benchmark and test behavior aligned.
    /// </summary>
    public Fixed64 StuckThresholdSpeed => (Fixed64)10;

    public Fixed64 Size { get; set; } = Fixed64.One;

    public Fixed64 Radius => Size * Fixed64.Half;

    public byte OccupantGroupId { get; set; } = 1;

    public BenchmarkSteerAgent(Vector3d position = default)
    {
        Position = position;
    }
}
