using Trailblazer.Pathing.Navigators;

namespace Trailblazer.Pathing
{
    public enum TrailGuideParadigm
    {
        None,
        Astar,
        FlowField
    }

    /// <summary>
    /// Provides a factory to request pathing guides based on the selected paradigm.
    /// </summary>
    public static class TrailGuideFactory
    {
        /// <summary>
        /// Instantiates a new guide (A* or FlowField) and initializes it for use.
        /// </summary>
        public static IGuide RequestGuide(TrailGuideParadigm trailGuideRequest)
        {
            IGuide result = null;

            switch (trailGuideRequest)
            {
                case TrailGuideParadigm.None:
                    return result;
                case TrailGuideParadigm.Astar:
                    result = RequestAstarGuide();
                    break;
                case TrailGuideParadigm.FlowField:
                    result = RequestFlowFieldGuide();
                    break;
                default:
                    return result;
            }

            result.OnSetup();
            return result;
        }

        public static AStarGuide RequestAstarGuide() => new();

        public static FlowFieldGuide RequestFlowFieldGuide() => new();
    }
}
