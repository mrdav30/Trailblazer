using FixedMathSharp;
using System;

namespace Trailblazer.Navigation;

/// <summary>
/// Defines event-driven interactions for the scout, including movement, forces, and state transitions.
/// </summary>
public class NavSteeringEvents
{
    /// <summary>
    /// Gets or sets the callback that is invoked when a move request is applied.
    /// </summary>
    /// <remarks>Assign a method to this delegate to execute custom logic after a move request has been processed.</remarks>
    public Action? OnMoveRequestApplied;

    /// <summary>
    /// Occurs when traversal is started, providing the initial position as a parameter.
    /// </summary>
    /// <remarks>Assign a handler to this event to perform custom logic when traversal begins.</remarks>
    public Action<Vector3d>? OnStartTraversal;

    /// <summary>
    /// Gets or sets the action to invoke when an invalid path is encountered.
    /// </summary>
    /// <remarks>Assign a delegate to handle scenarios where a provided path does not meet validation criteria.</remarks>
    public Action? OnInvalidPath;

    /// <summary>
    /// Gets or sets the callback that is invoked when the stuck condition is detected.
    /// </summary>
    /// <remarks>Assign a method to this delegate to handle scenarios where the object becomes stuck.</remarks>
    public Action? OnIsStuck;

    /// <summary>
    /// Called when unit arrives at destination
    /// </summary>
    public Action? OnArrive;

    /// <summary>
    /// Called whenever movement is stopped
    /// </summary>
    public Action? OnStopMove;
}
