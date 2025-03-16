using FixedMathSharp;
using Trailblazer;
using Trailblazer.Controllers;

public class MockScout : Scout
{
    public Vector3d Velocity { get; set; }

    private Vector3d _pendingVelocity;

    private Vector3d _positionDelta;

    private FixedQuaternion _rotationDelta = FixedQuaternion.Identity;

    private TraversalCondition _holdTraversal;

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
            _pendingVelocity += force * TrailblazerManager.DeltaTime;
        };
    }

    public override void SetTraversalState(TraversalMedium medium, Fixed64? surfaceLevel = null, GroundState? movementState = null)
    {
        base.SetTraversalState(medium, surfaceLevel, movementState);
        _holdTraversal = default;
    }

    public override void OnFinalizeTraversal()
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

        MockGroundCheck();

        Velocity = (WorldPosition - previousPosition) / TrailblazerManager.DeltaTime;

        base.OnFinalizeTraversal();
    }

    // Update TraversalState based on output from controller
    private void MockGroundCheck()
    {
        // mock grounding check
        if (_traversalState.Medium != TraversalMedium.Air && WorldPosition.y > _traversalState.SurfaceLevel + Fixed64.Epsilon)
        {
            //  hold what the previous medium was before switching to in air
            _holdTraversal = _traversalState;
            _traversalState.Medium = TraversalMedium.Air;
            _traversalState.Ground = null;
        }
        else if (_holdTraversal.Medium != TraversalMedium.Unknown && WorldPosition.y <= _traversalState.SurfaceLevel)
        {
            if (_holdTraversal.Medium == TraversalMedium.Water && _traversalState.Medium != TraversalMedium.Water
                || _holdTraversal.Medium == TraversalMedium.Ground && _traversalState.Medium != TraversalMedium.Ground)
            {
                _traversalState = _holdTraversal;
                _holdTraversal = default;
            }
        }

        // mock surface level check
        if (ScoutController.WasInAir)
        {
            if (_traversalState.Ground?.GroundNormal.y > Fixed64.Epsilon && WorldPosition.y < _traversalState.SurfaceLevel - Fixed64.Epsilon
                || ScoutController.IsInWater && WorldPosition.y > _traversalState.SurfaceLevel + Fixed64.Epsilon)
            {
                WorldPosition = new Vector3d(WorldPosition.x, _traversalState.SurfaceLevel, WorldPosition.z);
            }
        }

    }
}
