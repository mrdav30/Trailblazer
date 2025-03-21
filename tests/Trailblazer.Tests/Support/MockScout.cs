using FixedMathSharp;
using Trailblazer;
using Trailblazer.Controllers;

public class MockScout : Scout
{
    public Vector3d Velocity { get; set; }

    private Vector3d _pendingVelocity;

    private Vector3d _positionDelta;

    private FixedQuaternion _rotationDelta = FixedQuaternion.Identity;

    public MockScout(Vector3d position, Vector3d velocity, TraversalCondition traversalCondition)
    {
        WorldPosition = position;

        traversalCondition = MockGroundCheck(traversalCondition);

        base.OnInitialize(traversalCondition);

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

    public override void FinalizeTraversal()
    {
        Vector3d previousPosition = WorldPosition;

        //If grounded, cancel out excess downward movement from gravity, but preserve downward slope acceleration
        if (_traversalCondition.Medium == TraversalMedium.Ground && _pendingVelocity.y < Fixed64.Zero)
        {
            var groundForce = _controller.GravityForce * TrailblazerManager.DeltaTime; // gravity is defined as 9.8m/s^2
            _pendingVelocity.y = FixedMath.Min(_pendingVelocity.y + (groundForce * TrailblazerManager.DeltaTime), Fixed64.Zero);
        }

        // resolve velocity
        WorldPosition += _positionDelta + _pendingVelocity;

        _positionDelta = Vector3d.Zero;
        _pendingVelocity = Vector3d.Zero;

        if (_rotationDelta != FixedQuaternion.Identity)
        {
            VisualRotation *= _rotationDelta;
            _rotationDelta = FixedQuaternion.Identity;
        }

        _traversalCondition = MockGroundCheck(_traversalCondition);

        Velocity = (WorldPosition - previousPosition) / TrailblazerManager.DeltaTime;

        base.FinalizeTraversal();
    }

    // Update TraversalState based on output from controller
    private TraversalCondition MockGroundCheck(TraversalCondition condition)
    {
        // If scout is already grounded, maintain state unless velocity pushes it up
        if (condition.Medium == TraversalMedium.Ground)
        {
            if (Velocity.y > Fixed64.Zero)
            {
                // If scout is moving upwards, it should no longer be grounded
                condition.Medium = TraversalMedium.Air;
                return condition;
            }

            Vector3d groundNormal = condition.SurfaceCondition?.SurfaceNormal ?? Vector3d.Zero;
            Fixed64 groundY = GetSlopeSurfaceY(WorldPosition, groundNormal, condition.SurfaceLevel);

            if (WorldPosition.y < groundY)
                WorldPosition = new Vector3d(WorldPosition.x, groundY, WorldPosition.z);

            return condition;
        }

        // If scout is airborne, check if it should transition to grounded
        if (condition.Medium == TraversalMedium.Air)
        {
            Fixed64 surfaceLevel = condition.SurfaceLevel;
            Fixed64 scoutHeight = WorldPosition.y;

            // Ensure velocity is downward and scout is within landing range
            if (Velocity.y <= Fixed64.Zero && scoutHeight <= surfaceLevel + Fixed64.FromRaw(0x10000L)) // Small threshold
            {
                // Set state to grounded
                condition.Medium = TraversalMedium.Ground;
                WorldPosition = new Vector3d(WorldPosition.x, surfaceLevel, WorldPosition.z);

                // Update ground normal if needed (assuming ground is flat for now)
                condition.SurfaceCondition = new SurfaceCondition
                {
                    SurfaceMatrix = Fixed4x4.Identity, // Assuming a flat ground by default
                };
            }
        }

        return condition;
    }

    private Fixed64 GetSlopeSurfaceY(Vector3d position, Vector3d groundNormal, Fixed64 knownSurfaceY)
    {
        if (groundNormal.y.Abs() <= Fixed64.Epsilon)
            return knownSurfaceY; // Prevent divide-by-zero for vertical surfaces

        // Compute how much the scout has moved from the original surface detection point
        Fixed64 offset = (groundNormal.x * position.x + groundNormal.z * position.z) / groundNormal.y;

        // Adjust the surface level by the offset to get the true height at (x, z)
        return knownSurfaceY + offset;
    }
}
