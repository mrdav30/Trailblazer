using FixedMathSharp;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Creates built-in path requests for navigators from host-facing guided travel commands.
/// </summary>
public static class NavigatorPathRequestFactory
{
    public static bool TryCreate(
        Vector3d origin,
        Vector3d targetPosition,
        Fixed64 unitSize,
        GuidedPathMode pathMode,
        bool allowUnwalkable,
        bool allowTraversalTransitions,
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
        int flowFieldExtraFloodRange,
        TraversalMedium traversalMedium,
        out IPathRequest request)
    {
        switch (pathMode)
        {
            case GuidedPathMode.AStar:
                var aStar = AStarPathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkable,
                    allowTraversalTransitions);
                if (aStar == null)
                {
                    request = null;
                    return false;
                }

                aStar.MaxClimbHeight = aStarMaxClimbHeight;
                request = aStar;
                return true;

            case GuidedPathMode.FlowField:
                var flowField = FlowFieldPathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    allowUnwalkable,
                    allowTraversalTransitions);
                if (flowField == null)
                {
                    request = null;
                    return false;
                }

                flowField.ExtraFloodRange = flowFieldExtraFloodRange;
                request = flowField;
                return true;

            case GuidedPathMode.Aerial:
                var volume = VolumePathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkable,
                    VolumeTraversalMode.Open);
                if (volume == null)
                {
                    request = null;
                    return false;
                }

                request = volume;
                return true;

            case GuidedPathMode.Swim:
                if (traversalMedium != TraversalMedium.Water)
                {
                    request = null;
                    return false;
                }

                var swim = VolumePathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkable,
                    VolumeTraversalMode.Water);
                if (swim == null)
                {
                    request = null;
                    return false;
                }

                request = swim;
                return true;

            default:
                request = null;
                return false;
        }
    }
}
