using FixedMathSharp;

namespace Trailblazer.Navigation
{
    public interface IAvoidanceBody
    {
        Vector3d Position { get; }

        FixedQuaternion Rotation { get; }

        Vector3d Velocity { get; }

        Fixed64 Speed { get; }

        Fixed64 UnitSize { get; }

        Fixed64 UnitRadius { get; }

        bool IsAvoidingLeft { get; set; }
    }
}
