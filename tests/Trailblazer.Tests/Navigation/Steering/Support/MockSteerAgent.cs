using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using System;
using Trailblazer.Navigation;

namespace Trailblazer.Tests.Navigation.Steering
{
    public class MockSteerAgent : ISteer 
    {
        public Vector3d Position { get; private set; }
        public Vector3d Velocity { get; set; } = Vector3d.Zero;
        public FixedQuaternion Rotation { get; set; } = FixedQuaternion.Identity;
        public Vector3d AngularVelocity { get; set; } = Vector3d.Zero;
        public Vector3d Acceleration { get; set; } = Vector3d.Zero;

        public Fixed64 Size { get; set; } = Fixed64.One;
        public Fixed64 Radius => Size * Fixed64.Half;
        public Fixed64 Speed { get; set; } = Fixed64.Zero;
        public Fixed64 StuckThresholdSpeed => (Fixed64)10;

        public Guid GlobalId { get; private set; } = Guid.NewGuid();
        public byte OccupantGroupId { get; set; } = 1;
        public SwiftDictionary<GlobalVoxelIndex, int> OccupyingIndexMap { get; private set; } = new();

        public MockSteerAgent(Vector3d pos = default) => Position = pos;
        public void SetPosition(Vector3d p) => Position = p;
        public void SetOccupancy(GlobalVoxelIndex index, int ticket) => OccupyingIndexMap[index] = ticket;
        public void RemoveOccupancy(GlobalVoxelIndex index) => OccupyingIndexMap.Remove(index);
    }
}
