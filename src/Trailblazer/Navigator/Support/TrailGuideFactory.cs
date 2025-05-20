using Trailblazer.Pathing;
using Trailblazer.Pathing.Navigators;

namespace Trailblazer.Navigation
{
    public static class TrailGuideFactory
    {
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

        public static AStarGuide RequestAstarGuide() => new AStarGuide();

        public static FlowFieldGuide RequestFlowFieldGuide() => new FlowFieldGuide();
    }
}
