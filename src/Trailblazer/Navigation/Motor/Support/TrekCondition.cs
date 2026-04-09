using Chronicler;
using FixedMathSharp;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Represents the traversal state of a scout, including its movement medium and surface interactions.
/// </summary>
[Serializable]
public struct TrekCondition : IRecordable
{
    /// <summary>
    /// Defines the medium in which the scout is currently moving.
    /// </summary>
    public TraversalMedium Medium;

    /// <summary>
    /// Stores the height of the current surface, typically used for ground and water interactions.
    /// </summary>
    public Fixed64 SurfaceLevel;

    /// <summary>
    /// Stores the height of the ceiling above the scout, if applicable.
    /// Defaults to Fixed64.MAX_VALUE, meaning no ceiling.
    /// </summary>
    public Fixed64 CeilingLevel = Fixed64.MAX_VALUE;

    /// <summary>
    /// Contains data about the ground state, if applicable.
    /// </summary>
    public GroundCondition? GroundState;

    public TrekCondition() { }

    /// <summary>
    /// Creates a deep copy of the current <see cref="TrekCondition"/> instance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TrekCondition Clone() => new()
    {
        Medium = Medium,
        SurfaceLevel = SurfaceLevel,
        GroundState = GroundState?.Clone(),
        CeilingLevel = CeilingLevel
    };

    /// <summary>
    /// Resets the traversal condition to default values, indicating an unknown state.
    /// </summary>
    public void Reset()
    {
        Medium = TraversalMedium.Unknown;
        SurfaceLevel = Fixed64.Zero;
        GroundState = null;
        CeilingLevel = Fixed64.MAX_VALUE;
    }

    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref Medium, nameof(Medium), TraversalMedium.Unknown);
        RecordValues.Look(chronicler, ref SurfaceLevel, nameof(SurfaceLevel), Fixed64.Zero);
        RecordValues.Look(chronicler, ref CeilingLevel, nameof(CeilingLevel), Fixed64.MAX_VALUE);
        RecordNullableDeep.Look(chronicler, ref GroundState, nameof(GroundState));
    }
}
