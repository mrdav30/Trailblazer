using FixedMathSharp;

namespace Trailblazer.Tests.Navigation.Turning;

public class MockTurnAgent
{
    public Vector3d Position { get; set; }
    public Vector3d LastPosition { get; set; }
    public Vector3d Forward { get; set; }
    public FixedQuaternion Rotation { get; set; }
}
