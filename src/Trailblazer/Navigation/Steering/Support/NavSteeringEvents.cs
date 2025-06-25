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

        public Action? OnMoveRequestApplied;

        public Action<Vector3d>? OnStartTraversal;

        public Action? OnInvalidPath;

        public Action? OnIsStuck;

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
