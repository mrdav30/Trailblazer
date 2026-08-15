using System;
using FixedMathSharp;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer.Tests.Navigation.Steering;

public class MockSteerAgent : ISteer
{
    public Guid GlobalId { get; private set; } = Guid.NewGuid();
    public Vector3d Position { get; private set; }

    public Vector3d Velocity { get; set; } = Vector3d.Zero;
    public Fixed64 Speed { get; set; } = Fixed64.Zero;
    public Vector3d Acceleration { get; set; } = Vector3d.Zero;
    public Fixed64 StuckThresholdSpeed => (Fixed64)10;

    public NavigationAgentProfile NavigationProfile { get; set; } = PathTestFactory.DefaultNavigationProfile;
    public KinematicBodyShape BodyShape => NavigationProfile.Shape;
    public Fixed64 Radius => BodyShape.Radius;

    public Fixed64 Size
    {
        get => Radius + Radius;
        set => NavigationProfile = new NavigationAgentProfile(
            new KinematicBodyShape(value * Fixed64.Half, Fixed64.One, Fixed64.Quarter),
            Fixed64.One,
            Fixed64.One,
            Fixed64.Half,
            TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid,
            TraversalCapability.Jump
                | TraversalCapability.Climb
                | TraversalCapability.Swim
                | TraversalCapability.Fly);
    }

    public byte OccupantGroupId { get; set; } = 1;

    public MockSteerAgent(Vector3d pos = default) => Position = pos;
}
