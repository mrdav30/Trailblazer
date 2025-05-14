using FixedMathSharp;

namespace Trailblazer.Navigation
{
    public interface IAvoidanceBody
    {
        Vector3d Position { get; }
        Vector3d Velocity { get; }
        Fixed64 Radius { get; }
        bool IsAvoidingLeft { get; set; }
    }
}
