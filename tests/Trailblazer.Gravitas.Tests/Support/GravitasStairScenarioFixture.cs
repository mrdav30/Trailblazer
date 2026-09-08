using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.Materials;
using Gravitas.Support;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer.Gravitas.Tests.Support;

internal sealed class GravitasStairScenarioFixture : GravitasNavigatorScenarioFixture
{
    private readonly List<LSCuboidCollider> _stairColliders = new();
    private readonly List<Fixed64> _stairSurfaceLevels = new();

    internal GravitasStairScenarioFixture()
        : base(addWall: false, addNavigation: false)
    {
        ConfigureStairs();
    }

    protected override LSCollider CreateActorCollider(
        NavigationAgentProfile profile) => new LSCylinderCollider
    {
        Radius = Profile.Shape.Radius,
        Size = new Vector3d(
            Profile.Shape.Radius * 2,
            Profile.Shape.Height,
            Profile.Shape.Radius * 2)
    };

    protected override void PrepareHostMotion(GravitasNavigatorActor actor) =>
        ApplyGroundStep(actor);

    private void ConfigureStairs()
    {
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Half,
            (Fixed64)2);
        Fixed64[] surfaceLevels =
        {
            Fixed64.FromFraction(-9, 8),
            Fixed64.FromFraction(-3, 8),
            Fixed64.FromFraction(3, 8),
            Fixed64.FromFraction(9, 8)
        };
        var bindings = new NormalizedGridConfiguration[surfaceLevels.Length];
        var operations = new NavigationMapCommitOperation[surfaceLevels.Length];
        for (int step = 0; step < surfaceLevels.Length; step++)
            bindings[step] = AddStairNavigationGrid(step, surfaceLevels[step]);

        for (int step = 0; step < surfaceLevels.Length; step++)
        {
            string mapId = $"gravitas-stair-{step}";
            NavigationMap map = new NavigationMapBuilder(mapId, bindings[step])
                .AddCell(default, cell)
                .Build();
            operations[step] = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, 1),
                OverlayReplacementPolicy.Clear,
                3 + step,
                Trailblazer.FrameCount + 1);
            Trailblazer.Pathing.Admit(operations[step]).Should().BeTrue(
                "stair map {0} should be admitted at frame {1}",
                step,
                Trailblazer.FrameCount);
        }
        SimulateUntilApplied(operations);
        SimulateUntilSingleComponent(surfaceLevels.Length);

        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.FromFraction(1, 16),
                Fixed64.Quarter,
                Fixed64.FromFraction(1, 8)),
            Fixed64.FromFraction(3, 4),
            Fixed64.FromFraction(3, 4),
            Fixed64.FromFraction(1, 16),
            TraversalMedia.Solid,
            TraversalCapability.None);
        Vector3d startFoot = Anchor(bindings[0], default);
        Vector3d endFoot = Anchor(bindings[^1], default);
        ConfigureScenario(
            profile,
            startFoot,
            endFoot,
            "gravitas-stair-0",
            "gravitas-stair-3",
            PhysicsLayerMask.FromLayer(0),
            PhysicsLayerMask.None);

        for (int step = 0; step < bindings.Length; step++)
            AddStairBlock(step, Anchor(bindings[step], default).Y);
    }

    private void AddStairBlock(int step, Fixed64 top)
    {
        Fixed64[] treadMinimums =
        {
            -Fixed64.One,
            Fixed64.FromFraction(13, 16),
            Fixed64.FromFraction(21, 16),
            Fixed64.FromFraction(45, 16)
        };
        Fixed64[] treadMaximums =
        {
            Fixed64.FromFraction(13, 16),
            Fixed64.FromFraction(21, 16),
            Fixed64.FromFraction(45, 16),
            (Fixed64)4
        };
        Fixed64 treadMinimum = treadMinimums[step];
        Fixed64 treadMaximum = treadMaximums[step];
        Fixed64 height = top + (Fixed64)2;
        var collider = new LSCuboidCollider
        {
            Size = new Vector3d(
                treadMaximum - treadMinimum,
                height,
                Fixed64.One),
            Layer = new PhysicsLayer(0),
            IgnoredCollisionLayers = PhysicsLayerMask.None,
            Material = PhysicsMaterial.Frictionless
        };
        collider.InitializeWithNoBody(new GravitasFixtureMatterAgent(
            Gravitas,
            new FixedTransform(
                new Vector3d(
                    (treadMinimum + treadMaximum) * Fixed64.Half,
                    (top - (Fixed64)2) / (Fixed64)2,
                    Fixed64.Zero),
                FixedQuaternion.Identity,
                Vector3d.One)));
        AddFixtureCollider(collider);
        _stairColliders.Add(collider);
        _stairSurfaceLevels.Add(top);
    }

    // The scenario host owns step requests. It raises a body with a bounded
    // clearance only when the leading footprint finds a legal authored tread;
    // Gravitas then owns the full-height collision check and accepted pose.
    private void ApplyGroundStep(GravitasNavigatorActor actor)
    {
        Vector3d acceptedRoot = actor.Body.Position3d;
        Vector3d requestedRoot = actor.Body.PositionTransform.WorldPosition;
        Fixed64 rootToFoot = actor.Navigator.BodyShape.RootToFootOffsetY;
        Fixed64 stepClearance = Fixed64.FromFraction(1, 64);
        Fixed64 stepProbeTolerance = Fixed64.FromFraction(1, 16);
        if (actor.PendingStepCollider != null)
        {
            Fixed64 targetCenterX = actor.PendingStepCollider.Bounds.Min.X
                + actor.Navigator.BodyShape.Radius
                + stepClearance;
            bool reachedTargetCenter = acceptedRoot.X >= targetCenterX;
            var clearanceRequest = new Vector3d(
                reachedTargetCenter
                    ? acceptedRoot.X
                    : FixedMath.Min(
                        acceptedRoot.X + Fixed64.FromFraction(1, 32),
                        targetCenterX),
                actor.PendingStepSurface
                    + rootToFoot
                    + (reachedTargetCenter ? Fixed64.Zero : stepClearance),
                requestedRoot.Z);
            if (reachedTargetCenter)
                actor.PendingStepCollider = null;

            actor.Body.PositionTransform.LocalPosition = clearanceRequest;
            return;
        }

        Vector3d horizontalTravel = requestedRoot - acceptedRoot;
        horizontalTravel.Y = Fixed64.Zero;
        Vector3d direction;
        if (horizontalTravel == Vector3d.Zero)
        {
            if (actor.Navigator.Steering!.TargetDirection.Y <= Fixed64.Zero)
            {
                ApplyGroundSnap(actor, acceptedRoot, requestedRoot, rootToFoot);
                return;
            }
            direction = Vector3d.Right;
        }
        else
        {
            direction = horizontalTravel.Normalized;
        }
        Fixed64 currentSurface = acceptedRoot.Y - rootToFoot;
        Fixed64 maxStepUp = actor.Navigator.NavigationProfile.MaxStepUp;
        Vector3d requestedFoot = requestedRoot
            + Vector3d.Down * rootToFoot;
        Vector3d leadingFoot = requestedFoot
            + direction * (actor.Navigator.BodyShape.Radius + stepProbeTolerance);
        if (!TryGetStairAtPoint(
                leadingFoot,
                out LSCollider? targetCollider,
                out Fixed64 targetSurface))
        {
            ApplyGroundSnap(actor, acceptedRoot, requestedRoot, rootToFoot);
            return;
        }

        Fixed64 stepHeight = targetSurface - currentSurface;
        if (stepHeight <= Fixed64.Zero
            || stepHeight > maxStepUp
            || targetCollider == null)
        {
            ApplyGroundSnap(actor, acceptedRoot, requestedRoot, rootToFoot);
            return;
        }

        actor.PendingStepCollider = targetCollider;
        actor.PendingStepSurface = targetSurface;
        actor.Body.PositionTransform.LocalPosition = new Vector3d(
            acceptedRoot.X,
            targetSurface + rootToFoot + stepClearance,
            acceptedRoot.Z);
    }

    private void ApplyGroundSnap(
        GravitasNavigatorActor actor,
        Vector3d acceptedRoot,
        Vector3d requestedRoot,
        Fixed64 rootToFoot)
    {
        Fixed64 currentSurface = acceptedRoot.Y - rootToFoot;
        Fixed64 snapTolerance = Fixed64.FromFraction(1, 64);
        Vector3d requestedFoot = requestedRoot + Vector3d.Down * rootToFoot;
        if (!TryGetStairAtPoint(requestedFoot, out _, out Fixed64 targetSurface)
            || targetSurface > currentSurface + snapTolerance
            || currentSurface - targetSurface
                > actor.Navigator.NavigationProfile.MaxDropDown + snapTolerance)
        {
            return;
        }

        if (currentSurface - targetSurface <= snapTolerance)
        {
            requestedRoot.Y = acceptedRoot.Y;
            actor.Body.PositionTransform.LocalPosition = requestedRoot;
        }
    }

    private bool TryGetStairAtPoint(
        Vector3d point,
        out LSCollider? collider,
        out Fixed64 surfaceLevel)
    {
        collider = null;
        surfaceLevel = Fixed64.MinValue;
        for (int i = 0; i < _stairColliders.Count; i++)
        {
            LSCuboidCollider candidate = _stairColliders[i];
            if (point.X < candidate.Bounds.Min.X
                || point.X > candidate.Bounds.Max.X
                || point.Z < candidate.Bounds.Min.Z
                || point.Z > candidate.Bounds.Max.Z
                || _stairSurfaceLevels[i] <= surfaceLevel)
            {
                continue;
            }

            collider = candidate;
            surfaceLevel = _stairSurfaceLevels[i];
        }

        return collider != null;
    }

    private NormalizedGridConfiguration AddStairNavigationGrid(
        int step,
        Fixed64 surfaceLevel)
    {
        Fixed64 cellHeight = Fixed64.FromFraction(3, 4);
        Fixed64 cellWidth = step % 2 == 0
            ? (Fixed64)2
            : Fixed64.One;
        var center = new Vector3d(
            (Fixed64)step,
            surfaceLevel + cellHeight * Fixed64.Half,
            Fixed64.Zero);
        var configuration = new GridConfiguration(
            center,
            center,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                cellWidth,
                cellHeight,
                Fixed64.One));
        World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        return binding;
    }
}
