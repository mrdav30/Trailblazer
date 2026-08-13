using System.Runtime.CompilerServices;
using FixedMathSharp;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Tests.Navigation.Motor;

public class MockMotorAgent
{
    private TraversalMedium? _previousMedium;

    public Vector3d Position { get; set; }

    public Vector3d LastPosition { get; set; }

    public FixedQuaternion Rotation { get; set; }

    public Vector3d Velocity { get; set; }

    public TrekCondition FrameCondition;

    public TrekRequest FrameRequest;

    private Vector3d _positionDelta;

    private FixedQuaternion _rotationDelta = FixedQuaternion.Identity;

    private Vector3d _velocityDelta;

    public NavMotor Motor { get; set; }

    public MockMotorAgent(
        Vector3d position,
        TrekCondition condition,
        FixedQuaternion? rotation = null,
        Vector3d? velocity = null,
        LocomotionProfile? profile = null)
    {
        LastPosition = Position = position;

        FrameCondition = condition;
        FrameRequest = new();

        Rotation = rotation ?? FixedQuaternion.Identity;
        Velocity = velocity ?? Vector3d.Zero;

        Motor = NavMotor.CreateNew(TestWorld.Context, FrameCondition, profile);
        Motor.SetVelocity(Velocity);

        CheckTrekCondition();
        Motor.SyncTraversalState(FrameCondition, true);
    }

    public void Simulate()
    {
        FrameRequest.Origin = Position;
        FrameRequest.FootPosition = GetFootPosition();
        FrameRequest.Rotation = Rotation;

        if (Motor.TryTraversal(FrameRequest, out var velocityDelta, out var positionDelta, out var rotationDelta))
        {
            AddVelocityDelta(velocityDelta);
            AddPositionDelta(positionDelta);
            ApplyRotationDelta(rotationDelta);
        }

        LastPosition = Position;
        Position += _positionDelta + _velocityDelta;

        if (_rotationDelta != FixedQuaternion.Identity)
        {
            Rotation *= _rotationDelta;
            _rotationDelta = FixedQuaternion.Identity;
        }

        CheckTrekCondition();

        Fixed64 invDelta = TestWorld.Context.InvDeltaTime;
        Velocity = (Position - LastPosition) * invDelta;

        _positionDelta = Vector3d.Zero;
        _velocityDelta = Vector3d.Zero;

        Motor.FinalizeTraversal(Position, LastPosition, Rotation, FrameCondition, GetFootPosition());

        // Reset travel request for next frame
        FrameRequest.Reset();
    }

    // Update TraversalState based on output from controller
    public void CheckTrekCondition()
    {
        // If scout is already grounded, maintain state unless velocity pushes it up
        if (FrameCondition.Medium == TraversalMedium.Solid)
        {
            if (_velocityDelta.Y > Fixed64.Zero)
            {
                // If scout is moving upwards, it should no longer be grounded
                _previousMedium = FrameCondition.Medium;
                FrameCondition.Medium = TraversalMedium.Gas;
                return;
            }

            var platform = FrameCondition.GroundState?.Platform;
            if (platform?.Active == true)
            {
                // Compute world Y value from surface plane based on scout's X/Z
                Vector3d localPosition = platform.Value.Transform.InverseTransformPoint(Position);
                localPosition.Y = Fixed64.Zero; // align to the platform's base plane
                Vector3d alignedWorld = platform.Value.Transform.TransformPoint(localPosition);

                // Note: agent must be fully snapped to slope plane or velocity won't match projection.
                if (Position.Y < alignedWorld.Y)
                    Position = alignedWorld;
            }

            return;
        }

        // If scout is airborne, check if it should transition to grounded
        if (FrameCondition.Medium == TraversalMedium.Gas)
        {
            Fixed64 surfaceLevel = FrameCondition.SurfaceLevel;
            Fixed64 scoutHeight = Position.Y;

            // Ensure velocity is downward and scout is within landing range
            if (_velocityDelta.Y < Fixed64.Zero && scoutHeight <= surfaceLevel + Fixed64.FromRaw(0x10000L)) // Small threshold
            {
                // Set state to previous state or assume ground
                FrameCondition.Medium = _previousMedium ?? TraversalMedium.Solid;
                Position = new Vector3d(Position.X, surfaceLevel, Position.Z);

                if (FrameCondition.Medium == TraversalMedium.Solid)
                {
                    // Update ground normal if needed (assuming ground is flat for now)
                    FrameCondition.GroundState ??= new GroundCondition
                    {
                        Platform = default, // Assuming a flat ground by default
                    };
                }
            }

            return;
        }

        if (FrameCondition.Medium == TraversalMedium.Liquid)
        {
            Fixed64 surfaceLevel = FrameCondition.SurfaceLevel;
            Fixed64 scoutHeight = Position.Y;

            if (scoutHeight > surfaceLevel)
            {
                if (_velocityDelta.Y > Fixed64.Zero)
                {
                    // If scout is moving upwards, it should no longer be grounded
                    _previousMedium = FrameCondition.Medium;
                    FrameCondition.Medium = TraversalMedium.Gas;
                    return;
                }

                Position = new Vector3d(Position.X, surfaceLevel, Position.Z);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void AddPositionDelta(Vector3d delta)
    {
        if (delta == Vector3d.Zero) return;

        _positionDelta += delta;
        // shift last position so it doesn't alter object's velocity
        LastPosition += delta;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void ApplyRotationDelta(FixedQuaternion delta)
    {
        if (delta == FixedQuaternion.Identity) return;

        _rotationDelta *= delta;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void AddVelocityDelta(Vector3d delta)
    {
        if (delta == Vector3d.Zero) return;

        // assume a mass of 1...for now
        _velocityDelta += delta;
    }

    public virtual Vector3d GetFootPosition()
    {
        return Position + Vector3d.Down * Fixed64.Half;
    }
}
