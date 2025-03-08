using FixedMathSharp;
using Trailblazer;
using Trailblazer.Controllers;

public class MockScout : IScout
{
    private Vector3d _position;
    public Vector3d WorldPosition
    {
        get => _position;
        set
        {
            _position = value;
            // Simple ground check
            if (WorldPosition.y <= Traversal.SurfaceLevel)
                WorldPosition = new Vector3d(WorldPosition.x, Fixed64.Zero, WorldPosition.y);
        }
    }

    public FixedQuaternion VisualRotation { get; set; } = FixedQuaternion.Identity;

    public Vector3d LinearVelocity { get; set; }

    public ScoutEvents Events { get; set; } = new();

    public ScoutController ScoutController { get; set; }

    public Fixed64 Gravity { get; set; } = TrailblazerManager.GravityForce;

    public TraversalState Traversal;

    public MockScout(Vector3d position, Vector3d velocity)
    {
        WorldPosition = position;
        LinearVelocity = velocity;

        Events.CanAffordJump = () => true;

        Events.OnAddPositionDelta += deltaPos => WorldPosition += deltaPos;
        Events.OnAddRotationDelta += (rot) =>
        {
            VisualRotation *= rot;
        };
        Events.OnAddLinearForce += (force) =>
        {
            // assume a mass of 1
            // multiply force by DeltaTime to integrate as velocity delta
            LinearVelocity += force * TrailblazerManager.DeltaTime;

            // we should probably move this into a Simulate loop for IScout
            WorldPosition += LinearVelocity * TrailblazerManager.DeltaTime;
        };
        Events.OnAddAngularForce += (force) =>
        {
            FixedQuaternion deltaRotation = FixedQuaternion.FromAxisAngle(
                force.Normal,
                force.Magnitude * TrailblazerManager.DeltaTime
            );

            VisualRotation = deltaRotation * VisualRotation;
        };

        ScoutController = ScoutController.CreateNew(this);
        return;
    }

    public void SetTraversalState(TraversalMedium medium, Fixed64? surfaceLevel = null, GroundState? movementState = null)
    {
        Traversal.Medium = medium;
        Traversal.SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
        Traversal.Ground = movementState ?? GroundState.DefaultGroundState;
    }

    public void GetTraversalState(out TraversalState movementState)
    {
        if (Traversal.Medium != TraversalMedium.Water)
        {
            if (WorldPosition.y > Traversal.SurfaceLevel + Fixed64.FromRaw(0x1000))
                Traversal.Medium = TraversalMedium.Air;
            else if (WorldPosition.y <= Traversal.SurfaceLevel)
                Traversal.Medium = TraversalMedium.Ground;
        }

        movementState = Traversal;
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
        ScoutController.SetMotorLock(false);
    }
}
