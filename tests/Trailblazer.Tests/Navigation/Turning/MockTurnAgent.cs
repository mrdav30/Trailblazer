using FixedMathSharp;
using Trailblazer.Navigation.Turning;

namespace Trailblazer.Tests.Navigation.Turning
{
    public class MockTurnAgent : ITurn
    {
        public Vector3d Position { get; set; }
        public Vector3d LastPosition { get; set; }
        public Vector3d Forward { get; set; }
        public FixedQuaternion Rotation { get; set; }

        public void ApplyRotation(FixedQuaternion r) => Rotation = r;
    }
}
