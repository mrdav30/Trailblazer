using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Resolves whether a guided request or handoff currently implies climb intent.
/// </summary>
internal static class GuidedClimbIntentResolver
{
    public static bool Resolve(IPathRequest pathRequest, GuidedVolumeExitHandoff handoff = null)
    {
        if (handoff != null
            && TraversalTransitionRegistry.TryGet(handoff.TransitionId, out TraversalTransition transition))
        {
            return transition.RequestsClimbIntent;
        }

        return pathRequest switch
        {
            AStarPathRequest aStar => RequestsClimbIntent(HybridPathRequest.CreateFromAStar(aStar)),
            FlowFieldPathRequest flowField => RequestsClimbIntent(HybridPathRequest.CreateFromFlowField(flowField)),
            HybridPathRequest hybrid => RequestsClimbIntent(hybrid),
            _ => false
        };
    }

    private static bool RequestsClimbIntent(HybridPathRequest request)
    {
        TraversalTransition[] directedTransitions = request?.RoutePlan?.DirectedTransitions;
        if (directedTransitions == null)
            return false;

        for (int i = 0; i < directedTransitions.Length; i++)
        {
            if (directedTransitions[i].RequestsClimbIntent)
                return true;
        }

        return false;
    }
}
