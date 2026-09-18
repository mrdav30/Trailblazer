//=======================================================================
// NavigationAcquisitionMode.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation.Steering;

/// <summary>Selects how mandatory initial and recovery guidance is acquired.</summary>
public enum NavigationAcquisitionMode : byte
{
    /// <summary>Completes acquisition during the steering call.</summary>
    Immediate,
    /// <summary>Waits for the context's bounded scheduled acquisition and validation turns.</summary>
    Scheduled
}
