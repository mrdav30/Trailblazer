using FixedMathSharp;

namespace Trailblazer.Controllers
{
    /// <summary>
    /// The base interface for the scout.
    /// </summary>
    public interface IScout
    {
        public Vector3d WorldPosition { get; }

        public FixedQuaternion VisualRotation { get; }

        ScoutController ScoutController { get; }

        /// <summary>
        /// The events of the scout.
        /// </summary>
#nullable enable
        ScoutEvents? Events { get; }
#nullable disable
        
        void GetTraversalState(out TraversalState traversalState);

        Vector3d GetFootPosition();
    }
}
