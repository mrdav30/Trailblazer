//=======================================================================
// NavigationAgentProfileRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler;
using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>Records one exact immutable navigation-agent profile.</summary>
internal sealed class NavigationAgentProfileRecord : IRecordable
{
    public NavigationAgentProfileRecord()
    {
    }

    public NavigationAgentProfileRecord(NavigationAgentProfile profile) => Capture(profile);

    public Fixed64 Radius;

    public Fixed64 Height;

    public Fixed64 RootToFootOffsetY;

    public Fixed64 MaxStepUp;

    public Fixed64 MaxDropDown;

    public Fixed64 ArrivalRadius;

    public TraversalMedia AllowedMedia;

    public TraversalCapability Capabilities;

    public void Capture(NavigationAgentProfile profile)
    {
        Radius = profile.Shape.Radius;
        Height = profile.Shape.Height;
        RootToFootOffsetY = profile.Shape.RootToFootOffsetY;
        MaxStepUp = profile.MaxStepUp;
        MaxDropDown = profile.MaxDropDown;
        ArrivalRadius = profile.ArrivalRadius;
        AllowedMedia = profile.AllowedMedia;
        Capabilities = profile.Capabilities;
    }

    public bool TryCreate(out NavigationAgentProfile profile)
    {
        profile = default;
        try
        {
            profile = new NavigationAgentProfile(
                new KinematicBodyShape(Radius, Height, RootToFootOffsetY),
                MaxStepUp,
                MaxDropDown,
                ArrivalRadius,
                AllowedMedia,
                Capabilities);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref Radius, "Radius", Fixed64.Zero);
        RecordValues.Look(chronicler, ref Height, "Height", Fixed64.Zero);
        RecordValues.Look(chronicler, ref RootToFootOffsetY, "RootToFootOffsetY", Fixed64.Zero);
        RecordValues.Look(chronicler, ref MaxStepUp, "MaxStepUp", Fixed64.Zero);
        RecordValues.Look(chronicler, ref MaxDropDown, "MaxDropDown", Fixed64.Zero);
        RecordValues.Look(chronicler, ref ArrivalRadius, "ArrivalRadius", Fixed64.Zero);
        RecordValues.Look(chronicler, ref AllowedMedia, "AllowedMedia", TraversalMedia.None);
        RecordValues.Look(chronicler, ref Capabilities, "Capabilities", TraversalCapability.None);
    }
}
