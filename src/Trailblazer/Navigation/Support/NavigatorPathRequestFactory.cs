using FixedMathSharp;
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
        HeuristicMethod aStarHeuristic,
        Fixed64 aStarMaxClimbHeight,
        int flowFieldExtraFloodRange,
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
                    allowUnwalkable);
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
                    allowUnwalkable);
                if (flowField == null)
                {
                    request = null;
                    return false;
                }

                flowField.ExtraFloodRange = flowFieldExtraFloodRange;
                request = flowField;
                return true;

            case GuidedPathMode.Aerial:
                var aerial = AerialPathRequest.Create(
                    origin,
                    targetPosition,
                    unitSize,
                    aStarHeuristic,
                    allowUnwalkable);
                if (aerial == null)
                {
                    request = null;
                    return false;
                }

                request = aerial;
                return true;

            default:
                request = null;
                return false;
        }
    }
}
