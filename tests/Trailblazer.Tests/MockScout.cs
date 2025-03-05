using FixedMathSharp;
using Trailblazer;
using Trailblazer.Controllers;

public class MockScout : IScout
{
    public Vector3d WorldPosition { get; set; }

    public FixedQuaternion VisualRotation { get; set; } = FixedQuaternion.Identity;

    public Vector3d LinearVelocity { get; set; }

    public ScoutEvents Events { get; set; } = new();

    public ScoutController ScoutMotor { get; set; }

    public Fixed64 Gravity { get; set; } = TrailblazerManager.GravityForce;

    public TraversalData TraversalState;

    public MockScout(Vector3d position, Vector3d velocity)
    {
        WorldPosition = position;
        LinearVelocity = velocity;

        Events.CanAffordJump = () => true;

        Events.OnAddPositionDelta += deltaPos => WorldPosition += deltaPos;
        Events.OnSetRotation += rot => VisualRotation = rot;
        Events.OnAddLinearImpulse += (impulse) =>
        {
            LinearVelocity += impulse * TrailblazerManager.DeltaTime;
            WorldPosition += LinearVelocity;
        };
        Events.OnAddAngularImpulse += (angularVelocity) =>
        {
            FixedQuaternion deltaRotation = FixedQuaternion.FromAxisAngle(
                angularVelocity.Normal,
                angularVelocity.Magnitude * TrailblazerManager.DeltaTime
            );

            VisualRotation = deltaRotation * VisualRotation;
        };

        ScoutMotor = ScoutController.CreateNew(this);
        return;
    }

    public void SetTraversalState(TraversalData traversalState)
    {
        TraversalState = traversalState;
    }

    public void GetTraversalState(out TraversalData traversalState)
    {
        traversalState = TraversalState;
    }

    public Vector3d GetFootPosition()
    {
        return WorldPosition + Vector3d.Down * Fixed64.FromRaw(0x40000000L);
    }

    public void SetGravity(Fixed64 newGravity)
    {
        Gravity = newGravity;
    }

    public void FinalizeMovement()
    {
        ScoutMotor.SetMotorLock(false);
    }
}
