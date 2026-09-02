//=======================================================================
// TransitState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Represents the traversal state for a object while using a <see cref="NavMotor"/>
///  and provides synchronization with <see cref="TrekCondition"/>.
/// </summary>
public class TransitState
{
    #region State Properties

    /// <summary>
    /// The current traversal medium.
    /// </summary>
    public TraversalMedium Medium { get; private set; }

    /// <summary>
    /// The height of the surface the entity is interacting with.
    /// </summary>
    public Fixed64 SurfaceLevel { get; private set; }

    /// <summary>
    /// The ceiling height above the entity.
    /// </summary>
    public Fixed64 CeilingLevel { get; private set; } = Fixed64.MaxValue;

    /// <summary>
    /// The ground state, if applicable.
    /// </summary>
    public GroundCondition? GroundState { get; private set; }

    /// <summary>
    /// The normal of the ground surface.
    /// </summary>
    public Vector3d SurfaceNormal { get; private set; }

    /// <summary>
    /// Gets the angle of the slope at the current position, in fixed-point degrees.
    /// </summary>
    public Fixed64 SlopeAngle { get; private set; }

    /// <summary>
    /// The previous traversal state (for comparison and transition detection).
    /// </summary>
    public TrekCondition? PreviousState { get; private set; }

    /// <summary>
    /// The previous traversal medium, treating a missing previous sample as <see cref="TraversalMedium.Unknown"/>.
    /// </summary>
    public TraversalMedium PreviousMedium => PreviousState?.Medium ?? TraversalMedium.Unknown;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the TransitState class using the specified trek condition.
    /// </summary>
    /// <param name="condition">The trek condition to use for initializing the state. Cannot be null.</param>
    public TransitState(TrekCondition condition) => Update(condition, null);

    /// <summary>
    /// Initializes a new instance of the TransitState class with the specified current and previous trek conditions.
    /// </summary>
    /// <param name="condition">The current trek condition to represent. Cannot be null.</param>
    /// <param name="previous">The previous trek condition, or null if there is no previous condition.</param>
    public TransitState(TrekCondition condition, TrekCondition? previous) => Update(condition, previous);

    #endregion

    #region State Update

    /// <summary>
    /// Updates the traversal state and retains the previous state for transition tracking.
    /// </summary>
    public void Update(TrekCondition condition, TrekCondition? previous)
    {
        PreviousState = previous;
        Medium = condition.Medium;
        SurfaceLevel = condition.SurfaceLevel;
        GroundState = condition.GroundState;

        if (Medium == TraversalMedium.Solid)
        {
            SurfaceNormal = GroundState?.GroundNormal ?? Vector3d.Zero;
            SlopeAngle = Vector3d.Angle(Vector3d.Up, SurfaceNormal);
        }
        else
        {
            SurfaceNormal = Vector3d.Up;
            SlopeAngle = Fixed64.Zero;
        }

        CeilingLevel = condition.CeilingLevel;
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Calculates the signed angle of the current surface slope relative to the specified movement direction.
    /// </summary>
    /// <param name="moveDirection">
    /// The movement direction vector used to determine the orientation relative to the slope.
    /// If this value is <see cref="Vector3d.Zero"/>, the method treats the movement as oriented downhill.
    /// </param>
    /// <returns>
    /// The signed slope angle in degrees.
    /// A negative value indicates movement downhill, and a positive value indicates movement uphill.
    /// Returns zero if the surface is flat.
    /// </returns>
    public Fixed64 GetSignedSlopeAngle(Vector3d moveDirection)
    {
        if (SlopeAngle == Fixed64.Zero)
            return Fixed64.Zero;

        // Treat downhill as gravity-biased when idle
        if (moveDirection == Vector3d.Zero)
            moveDirection = Vector3d.Backward;

        bool isDownhill = Vector3d.Dot(moveDirection.Normalized, SurfaceNormal) < Fixed64.Zero;
        return isDownhill ? -SlopeAngle : SlopeAngle;
    }

    /// <summary>
    /// Returns a new TraversalCondition instance reflecting the current state.
    /// </summary>
    public TrekCondition ToTrekCondition()
    {
        return new TrekCondition()
        {
            Medium = Medium,
            SurfaceLevel = SurfaceLevel,
            GroundState = GroundState,
            CeilingLevel = CeilingLevel
        };
    }

    #endregion
}
