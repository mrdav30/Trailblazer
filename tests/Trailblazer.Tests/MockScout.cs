using FixedMathSharp;
using Trailblazer;
using Trailblazer.Controllers;

public class MockScout : Scout
{
    public Vector3d Velocity { get; set; }

    private Vector3d _pendingVelocity;

    private Vector3d _positionDelta;

    private FixedQuaternion _rotationDelta = FixedQuaternion.Identity;

    public MockScout(Vector3d position, Vector3d velocity)
    {
        WorldPosition = position;

        base.OnInitialize();

        ScoutController.SetVelocity(velocity);

        Events.CanAffordJump = () => true;

        Events.OnAddPlatformPositionDelta += (deltaPos) =>
        {
            _positionDelta += deltaPos;
        };
        Events.OnAddPlatformRotationDelta += (rot) =>
        {
            _rotationDelta *= rot;
        };
        Events.OnAddLinearForce += (force) =>
        {
            // assume a mass of 1
            _pendingVelocity += force;
        };
    }

    /// <summary>
    /// Simulates the scout's traversal for a single frame by processing movement input and finalizing state updates.
    /// </summary>
    public override void OnSimulate()
    {
        StartTraversal();
        FinalizeTraversal();
    }

    public override void FinalizeTraversal()
    {
        Vector3d previousPosition = WorldPosition;

        // resolve velocity
        WorldPosition += _positionDelta + _pendingVelocity;

        _positionDelta = Vector3d.Zero;
        _pendingVelocity = Vector3d.Zero;

        if (_rotationDelta != FixedQuaternion.Identity)
        {
            VisualRotation *= _rotationDelta;
            _rotationDelta = FixedQuaternion.Identity;
        }

        Velocity = (WorldPosition - previousPosition) / TrailblazerManager.DeltaTime;

        MockGroundCheck();

        base.FinalizeTraversal();
    }

    // Update TraversalState based on output from controller
    private void MockGroundCheck()
    {
        // If scout is already grounded, maintain state unless velocity pushes it up
        if (_traversalState.Medium == TraversalMedium.Ground)
        {
            if (Velocity.y > Fixed64.Zero)
            {
                // If scout is moving upwards, it should no longer be grounded
                _traversalState.Medium = TraversalMedium.Air;
            }
            return;
        }

        // If scout is airborne, check if it should transition to grounded
        if (_traversalState.Medium == TraversalMedium.Air)
        {
            Fixed64 surfaceLevel = _traversalState.SurfaceLevel;
            Fixed64 scoutHeight = WorldPosition.y;

            // Ensure velocity is downward and scout is within landing range
            if (Velocity.y <= Fixed64.Zero && scoutHeight <= surfaceLevel + Fixed64.FromRaw(0x10000L)) // Small threshold
            {
                // Set state to grounded
                _traversalState.Medium = TraversalMedium.Ground;
                WorldPosition = new Vector3d(WorldPosition.x, surfaceLevel, WorldPosition.z);

                // Update ground normal if needed (assuming ground is flat for now)
                _traversalState.Ground = new GroundState
                {
                    GroundMatrix = Fixed4x4.Identity, // Assuming a flat ground by default
                };
            }
        }
    }
}
