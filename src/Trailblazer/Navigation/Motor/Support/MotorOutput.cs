using FixedMathSharp;

namespace Trailblazer.Navigation.Motor;

public struct MotorOutput
{
    public Vector3d VelocityDelta;

    public Vector3d PositionDelta;

    public FixedQuaternion RotationDelta;

    public MotorOutput(Vector3d velocityDelta, Vector3d positionDelta, FixedQuaternion rotationDelta)
    {
        VelocityDelta = velocityDelta;
        PositionDelta = positionDelta;
        RotationDelta = rotationDelta;
    }
}