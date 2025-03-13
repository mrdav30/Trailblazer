using FixedMathSharp;
using Trailblazer;
using Trailblazer.Controllers;

public class MockScout : IScout
{
    public Vector3d WorldPosition { get; set; }

    public FixedQuaternion VisualRotation { get; set; } = FixedQuaternion.Identity;

    public Vector3d LinearVelocity { get; set; }

    public ScoutEvents Events { get; set; } = new();

    public ScoutController ScoutController { get; set; }

    public TraversalState Traversal;

    public TraversalMedium _holdMedium;

    private TraversalRequest _traversalRequest;

    public MockScout(Vector3d position, Vector3d velocity)
    {
        WorldPosition = position;
        LinearVelocity = velocity;

        Events.CanAffordJump = () => true;

        Events.OnAddPositionDelta += (deltaPos) =>
        {
            WorldPosition += deltaPos;
        };
        Events.OnAddRotationDelta += (rot) =>
        {
            VisualRotation *= rot;
        };
        Events.OnAddLinearForce += (force) =>
        {
            // assume a mass of 1
            // multiply force by DeltaTime to integrate as velocity delta
            LinearVelocity += force * TrailblazerManager.DeltaTime;
        };
        Events.OnAddAngularForce += (force) =>
        {
            FixedQuaternion deltaRotation = FixedQuaternion.FromAxisAngle(
                force.Normal,
                force.Magnitude * TrailblazerManager.DeltaTime
            );

            VisualRotation *= deltaRotation;
        };

        ScoutController = ScoutController.CreateNew(this);
        return;
    }

    #region Pre-Simulate

    public void SetTraversalState(TraversalMedium medium, Fixed64? surfaceLevel = null, GroundState? movementState = null)
    {
        Traversal.Medium = medium;
        Traversal.SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
        Traversal.Ground = movementState ?? null;
    }

    public void SetTraversalRequest(Vector3d vector, TraversalSpeed traversalSpeed, bool isRequestingJump = false)
    {
        _traversalRequest = new TraversalRequest
        {
            MovementDirection = vector,
            TraversalSpeed = traversalSpeed,
            IsRequestingJump = isRequestingJump
        };
    }

    #endregion

    public void Simulate()
    {
        ScoutController.Simulate(_traversalRequest);

        // resolve velocity
        if (LinearVelocity != Vector3d.Zero)
            WorldPosition += LinearVelocity * TrailblazerManager.DeltaTime;

        MockGroundCheck();

        // since this is a mock we can unlock immediately, usually this would be called after the body has applied this movement
        UnlockController();

        _traversalRequest = default;
    }

    public void GetTraversalState(out TraversalState movementState)
    {
        movementState = Traversal;
    }

    // Update TraversalState based on output from controller
    private void MockGroundCheck()
    {
        // mock surface level check
        if (!ScoutController.Locomotions.Jump.IsJumping)
        {
            if (ScoutController.IsInAir
                && Traversal.Ground?.GroundNormal.y > Fixed64.Epsilon
                && WorldPosition.y < Traversal.SurfaceLevel - Fixed64.Epsilon)
            {
                WorldPosition = new Vector3d(WorldPosition.x, Traversal.SurfaceLevel, WorldPosition.z);
            }

            if (ScoutController.IsInWater
                && WorldPosition.y > Traversal.SurfaceLevel + Fixed64.Epsilon)
            {
                WorldPosition = new Vector3d(WorldPosition.x, Traversal.SurfaceLevel, WorldPosition.z);
            }
        }

        // mock grounding check
        if (Traversal.Medium != TraversalMedium.Air && WorldPosition.y > Traversal.SurfaceLevel + Fixed64.Epsilon)
        {
            //  hold what the previous medium was before switching to in air
            _holdMedium = Traversal.Medium;
            Traversal.Medium = TraversalMedium.Air;
        }
        else if (_holdMedium != TraversalMedium.Unknown && WorldPosition.y <= Traversal.SurfaceLevel)
        {
            if (_holdMedium == TraversalMedium.Water && Traversal.Medium != TraversalMedium.Water)
                Traversal.Medium = TraversalMedium.Water;
            else if (_holdMedium == TraversalMedium.Ground && Traversal.Medium != TraversalMedium.Ground)
                Traversal.Medium = TraversalMedium.Ground;
        }
    }

    public Vector3d GetFootPosition()
    {
        return WorldPosition + Vector3d.Down * Fixed64.FromRaw(0x40000000L);
    }

    public void UnlockController()
    {
        ScoutController.SetMotorLock(false);
    }
}
