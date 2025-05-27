using FixedMathSharp;
using System;

namespace Trailblazer.Navigation
{
    /// <summary>
    /// Defines event-driven interactions for the scout, including movement, forces, and state transitions.
    /// </summary>
    public class NavSteeringEvents
    {
#nullable enable

        public Action? OnStartMove;

        public Action<Vector3d>? OnStartGuidedTraversal;

        /// <summary>
        /// Called when unit arrives at destination
        /// </summary>
        public Action? OnArrive;

        /// <summary>
        /// Called whenever movement is stopped
        /// </summary>
        public Action? OnStopMove;

#nullable disable
    }
}
