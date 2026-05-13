using FixedMathSharp;
using System;
using Trailblazer.Navigation;

namespace Trailblazer.Benchmarks;

internal sealed class BenchmarkOccupant : ISteer
{
    public Guid GlobalId { get; } = Guid.NewGuid();

    public Vector3d Position { get; }

    public Vector3d Velocity { get; } = new(Fixed64.One, Fixed64.Zero, Fixed64.Zero);

    public Fixed64 Speed { get; } = Fixed64.One;

    public Vector3d Acceleration { get; } = Vector3d.Zero;

    public Fixed64 StuckThresholdSpeed => (Fixed64)10;

    public Fixed64 Size { get; } = Fixed64.One;

    public Fixed64 Radius => Size * Fixed64.Half;

    public byte OccupantGroupId { get; }

    public BenchmarkOccupant(Vector3d position, byte occupantGroupId)
    {
        Position = position;
        OccupantGroupId = occupantGroupId;
    }
}
