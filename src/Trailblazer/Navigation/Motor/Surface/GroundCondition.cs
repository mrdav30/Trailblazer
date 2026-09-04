//=======================================================================
// GroundCondition.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler;
using FixedMathSharp;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Represents the state of the surface the scout is interacting with, including surface movement and normal data.
/// </summary>
[Serializable]
public struct GroundCondition : IRecordable
{
    /// <summary>
    /// The host-provided sampled platform state the scout is currently standing on.
    /// </summary>
    public PlatformSnapshot Platform;

    /// <summary>
    /// Host-provided world-space normal of the sampled support surface.
    /// <see cref="Vector3d.Zero"/> indicates that no surface normal was sampled.
    /// </summary>
    public Vector3d SurfaceNormal;

    /// <summary>
    /// The current surface friction applied to movement.
    /// </summary>
    public Fixed64 SurfaceFriction;

    /// <summary>
    /// Determines how the scout inherits movement from the ground surface when the sampled platform is not inert.
    /// </summary>
    public MotionTransfer MotionTransferState;

    /// <summary>
    /// Creates a new GroundCondition instance that is a copy of the current instance.
    /// </summary>
    /// <remarks>
    /// The returned object is a shallow copy. Reference-type properties are not deeply cloned.
    /// </remarks>
    /// <returns>
    /// A new GroundCondition object with the same platform, surface normal, friction, and motion-transfer values as the current instance.
    /// </returns>
    public GroundCondition Clone() => new()
    {
        Platform = Platform,
        SurfaceNormal = SurfaceNormal,
        SurfaceFriction = SurfaceFriction,
        MotionTransferState = MotionTransferState
    };

    /// <inheritdoc/>
    public void RecordData(IChronicler chronicler)
    {
        chronicler.LookDeepStruct(ref Platform, "Platform");
        chronicler.LookValue(ref SurfaceNormal, "SurfaceNormal", Vector3d.Zero);
        chronicler.LookValue(ref SurfaceFriction, "SurfaceFriction", Fixed64.Zero);
        chronicler.LookValue(ref MotionTransferState, "MotionTransferState", MotionTransfer.None);
    }
}
