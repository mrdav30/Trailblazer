using FixedMathSharp;
using GridForge.Spatial;
using Trailblazer.Navigation;

namespace Trailblazer.Tests.Navigation.Steering
{
    public class MockSteerAgent : INavigate, IVoxelOccupant
    {
        public Vector3d Position { get; private set; }
        public Vector3d Velocity { get; set; } = Vector3d.Zero;
        public FixedQuaternion Rotation { get; set; } = FixedQuaternion.Identity;
        public Vector3d AngularVelocity { get; set; } = Vector3d.Zero;

        public Fixed64 UnitSize => Fixed64.One;
        public Fixed64 UnitRadius => Fixed64.Half;
        public Fixed64 Speed { get; set; } = Fixed64.Zero;
        public Fixed64 StuckThresholdSpeed => (Fixed64)10;
        public bool IsAvoidingLeft { get; set; } = false;

        public byte OccupantGroupId { get; set; } = 1;

        public bool IsVoxelOccupant { get ; set; }

        public int OccupantTicket { get ; set ; }

        public Vector3d WorldPosition => Position;

        public GlobalVoxelIndex GlobalIndex { get ; set; }

        public MockSteerAgent(Vector3d pos = default) => Position = pos;
        public void SetPosition(Vector3d p) => Position = p;

        public Vector3d GetFootPosition()
        {
            return Position + Vector3d.Down * Fixed64.Half;
        }
    }
}
