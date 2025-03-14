using FixedMathSharp;
using Trailblazer;
using Trailblazer.Controllers;

public class MockScout : IScout
{
    public Vector3d WorldPosition { get; set; }

    public FixedQuaternion VisualRotation { get; set; } = FixedQuaternion.Identity;

    public ScoutEvents Events { get; set; } = new();

    public ScoutController ScoutController { get; set; }
    public Vector3d Velocity { get; set; }

    private Vector3d _pendingVelocity;

    private Vector3d _positionDelta;

    private FixedQuaternion _rotationDelta = FixedQuaternion.Identity;

    private TraversalState _traversal;

    private TraversalMedium _holdMedium;

    private TraversalRequest _traversalRequest;

    public MockScout(Vector3d position, Vector3d velocity)
    {
        WorldPosition = position;
        _pendingVelocity = velocity;

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
            _pendingVelocity += force * TrailblazerManager.DeltaTime;
        };

        ScoutController = ScoutController.CreateNew(this);
        return;
    }

    #region Pre-Simulate

    public void SetTraversalState(TraversalMedium medium, Fixed64? surfaceLevel = null, GroundState? movementState = null)
    {
        _traversal.Medium = medium;
        _traversal.SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
        _traversal.Ground = movementState ?? null;
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

    public void InitiateTraversal()
    {
        ScoutController.Traverse(_traversalRequest);

        _traversalRequest = default;
    }

    public void GetTraversalState(out TraversalState movementState)
    {
        movementState = _traversal;
    }

    public void FinalizeTraversal()
    {
        Vector3d previousPosition = WorldPosition;

        // resolve velocity
        WorldPosition += _positionDelta + (_pendingVelocity * TrailblazerManager.DeltaTime);

        _positionDelta = Vector3d.Zero;
        _pendingVelocity = Vector3d.Zero;

        Velocity = (WorldPosition - previousPosition) / TrailblazerManager.DeltaTime;

        if (_rotationDelta != FixedQuaternion.Identity)
        {
            VisualRotation *= _rotationDelta;
            _rotationDelta = FixedQuaternion.Identity;
        }

        MockGroundCheck();

        ScoutController.Finalize(false);
    }

    // Update TraversalState based on output from controller
    private void MockGroundCheck()
    {
        // mock surface level check
        if (!ScoutController.Locomotions.Jump.IsJumping)
        {
            if (ScoutController.IsInAir
                && _traversal.Ground?.GroundNormal.y > Fixed64.Epsilon
                && WorldPosition.y < _traversal.SurfaceLevel - Fixed64.Epsilon)
            {
                WorldPosition = new Vector3d(WorldPosition.x, _traversal.SurfaceLevel, WorldPosition.z);
            }

            if (ScoutController.IsInWater
                && WorldPosition.y > _traversal.SurfaceLevel + Fixed64.Epsilon)
            {
                WorldPosition = new Vector3d(WorldPosition.x, _traversal.SurfaceLevel, WorldPosition.z);
            }
        }

        // mock grounding check
        if (_traversal.Medium != TraversalMedium.Air && WorldPosition.y > _traversal.SurfaceLevel + Fixed64.Epsilon)
        {
            //  hold what the previous medium was before switching to in air
            _holdMedium = _traversal.Medium;
            _traversal.Medium = TraversalMedium.Air;
        }
        else if (_holdMedium != TraversalMedium.Unknown && WorldPosition.y <= _traversal.SurfaceLevel)
        {
            if (_holdMedium == TraversalMedium.Water && _traversal.Medium != TraversalMedium.Water)
                _traversal.Medium = TraversalMedium.Water;
            else if (_holdMedium == TraversalMedium.Ground && _traversal.Medium != TraversalMedium.Ground)
                _traversal.Medium = TraversalMedium.Ground;
        }
    }

    public Vector3d GetFootPosition()
    {
        return WorldPosition + Vector3d.Down * Fixed64.FromRaw(0x40000000L);
    }
}
