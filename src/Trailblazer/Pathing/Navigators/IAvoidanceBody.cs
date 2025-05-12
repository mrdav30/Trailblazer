using FixedMathSharp;

namespace Trailblazer.Pathing
{
    public interface IAvoidanceBody
    {
        Vector3d Position { get; }
        Vector3d Velocity { get; }
        Fixed64 Radius { get; }
        bool IsAvoidingLeft { get; set; }
    }
}
