using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>
/// Provides steering direction based on a flow field vector grid.
/// Suitable for group-based or gradient-following movement strategies.
/// </summary>
public class FlowFieldGuide : IGuide
{
    /// <summary>
    /// The maximum distance to search for a valid flow vector when the agent is outside the flow field bounds.
    /// </summary>
    public static readonly Fixed64 DefaultFieldSearchRange = new(10);

    /// <summary>
    /// The result of the flow field survey, containing the vector field and path information needed to guide an agent along the flow.
    /// </summary>
    public FlowFieldSurveyResult FlowMap { get; private set; }

    #region Staged Guide State

    /// <summary>
    /// When executing a hybrid route plan, the guide may need to stage multiple sub-guides for each step of the plan (e.g. flow field for one segment, A* waypoints for another).
    /// </summary>
    private HybridRoutePlan _stagedPlan;

    /// <summary>
    /// The index of the currently active step within the staged plan. 
    /// This allows the guide to track progression through the plan and determine which sub-guide to use for providing directions.
    /// </summary>
    private int _stagedStepIndex;

    /// <summary>
    /// The currently active sub-guide for the active step in the staged plan. 
    /// This is used to delegate direction requests to the appropriate guide based on the current step type (e.g. flow field or A*).
    /// </summary>
    private IGuide _activeStageGuide;

    /// <summary>
    /// The index of the currently active step guide within the staged plan.
    /// </summary>
    private int _activeStageGuideStepIndex = -1;

    /// <summary>
    /// Indicates whether the guide is currently executing a staged plan with an active sub-guide.
    /// </summary>
    internal bool IsStaged => _stagedPlan != null;

    #endregion

    /// <summary>
    /// Initializes the guide with the given flow field survey result.
    /// </summary>
    /// <param name="surveyResult">The result of the flow field survey containing the vector field and path information.</param>
    /// <returns>True if the guide is successfully initialized with a valid path; otherwise, false.</returns>
    public bool Initialize(FlowFieldSurveyResult surveyResult)
    {
        if (!surveyResult.HasPath)
            return false;

        ReleaseStagedResources(dispose: false);
        FlowMap = surveyResult;

        return true;
    }

    /// <summary>
    /// Initializes the guide with a staged hybrid route plan, which may contain multiple steps with different guide types (e.g. flow field segments and A* waypoint segments).
    /// </summary>
    /// <param name="routePlan">The hybrid route plan containing multiple steps with different guide types.</param>
    /// <returns>True if the guide is successfully initialized with a valid staged plan; otherwise, false.</returns>
    internal bool InitializeStaged(HybridRoutePlan routePlan)
    {
        if (routePlan?.Steps == null || routePlan.Steps.Length == 0)
            return false;

        ReleaseStagedResources(dispose: false);
        FlowMap = null;
        _stagedPlan = routePlan;
        _stagedStepIndex = 0;
        _activeStageGuideStepIndex = -1;
        return true;
    }

    /// <inheritdoc/>
    public bool TryGetMovementDirection(Vector3d origin, out Vector3d direction)
    {
        direction = Vector3d.Zero;
        if (IsStaged)
            return TryGetStagedMovementDirection(origin, out direction);

        if (FlowMap == null || !FlowMap.HasPath)
            return false;

        direction = FlowFieldSurveyor.SampleFlowVector(origin, FlowMap.Fields);
        if (direction == Vector3d.Zero)
            return false;

        direction = direction.Normal;
        return true;
    }

    /// <summary>
    /// Determines whether the flow field contains a valid vector for the given position, which can be used to determine if the guide can provide directions from that position or if fallback logic should be used instead.
    /// </summary>
    /// <param name="origin">The position to check within the flow field.</param>
    /// <returns>True if the flow field contains a valid vector for the given position; otherwise, false.</returns>
    public bool FlowFieldContainsPosition(Vector3d origin)
    {
        if (IsStaged)
        {
            return _activeStageGuide is FlowFieldGuide stagedFlowGuide
                && stagedFlowGuide.FlowFieldContainsPosition(origin);
        }

        if (FlowMap == null
            || !FlowMap.HasPath
            || !TrailblazerWorldManager.TryGetVoxel(origin, out Voxel? currentVoxel)
            || !FlowMap.Fields.ContainsKey(currentVoxel!.WorldIndex))
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool TryGetFallbackDirection(Vector3d origin, out Vector3d fallbackDirection)
    {
        fallbackDirection = Vector3d.Zero;
        if (IsStaged)
            return TryGetStagedFallbackDirection(origin, out fallbackDirection);

        if (FlowMap == null
            || !FlowMap.HasPath
            || !TrailblazerWorldManager.TryGetVoxel(origin, out Voxel currentVoxel)
            || !FlowMap.Fields.ContainsKey(currentVoxel.WorldIndex))
        {
            return false;
        }

        // Once the current voxel is already part of the flow map, its center is the nearest valid anchor.
        fallbackDirection = (currentVoxel.WorldPosition - origin).Normalize();
        return true;
    }

    /// <summary>
    /// Releases any resources associated with the currently staged plan and active stage guide, optionally disposing of the active guide if it implements IDisposable.
    /// </summary>
    /// <param name="dispose">Indicates whether to dispose of the active guide if it implements IDisposable.</param>
    internal void ReleaseStagedResources(bool dispose)
    {
        if (_activeStageGuide != null)
        {
            PathGuideFactory.ReturnGuide(_activeStageGuide, dispose);
            _activeStageGuide = null;
        }

        _activeStageGuideStepIndex = -1;
        _stagedPlan = null;
        _stagedStepIndex = 0;
    }

    /// <summary>
    /// Attempts to get the movement direction from the currently active stage guide within the staged plan.
    /// </summary>
    /// <param name="origin">The position from which to determine the movement direction.</param>
    /// <param name="direction">The resulting movement direction, if available.</param>
    /// <returns>True if a valid movement direction was obtained; otherwise, false.</returns>
    private bool TryGetStagedMovementDirection(Vector3d origin, out Vector3d direction)
    {
        direction = Vector3d.Zero;
        int remainingStageAdvances = _stagedPlan.Steps.Length;
        while (TryGetPreparedStage(origin, ref remainingStageAdvances, out HybridRouteStep currentStep))
        {
            switch (currentStep.Kind)
            {
                case HybridRouteStepKind.Waypoint:
                    // TryGetPreparedStage(...) already skipped completed waypoint stages, so a yielded waypoint
                    // must still be ahead of the caller and therefore resolves to a non-zero direction.
                    direction = (currentStep.WaypointPosition - origin).Normalize();
                    return true;

                case HybridRouteStepKind.PathSegment:
                    if (!TryGetSegmentStageMovementDirection(
                        origin,
                        currentStep,
                        ref remainingStageAdvances,
                        out direction))
                    {
                        return false;
                    }

                    if (direction != Vector3d.Zero)
                        return true;

                    break;
            }
        }

        return false;
    }

    private bool TryGetSegmentStageMovementDirection(
        Vector3d origin,
        HybridRouteStep currentStep,
        ref int remainingStageAdvances,
        out Vector3d direction)
    {
        direction = Vector3d.Zero;
        if (!TryGetOrCreateActiveStageGuide(currentStep, out IGuide activeGuide))
            return false;

        if (activeGuide is IWaypointGuide waypointGuide)
            direction = waypointGuide.GetCurrentWaypointDirection(origin);
        else
            activeGuide.TryGetMovementDirection(origin, out direction);

        return direction != Vector3d.Zero
            || (IsStageTargetReached(origin, currentStep)
                && TryAdvanceStage(ref remainingStageAdvances));
    }

    /// <summary>
    /// Attempts to get a fallback movement direction from the currently active stage guide within the staged plan, starting with the current stage and searching forward through subsequent stages if necessary.
    /// This allows the guide to provide fallback directions from upcoming stages in the plan if the current stage does not yield a valid direction, which can help prevent the agent from getting stuck when transitioning between stages or when the current stage's guide is unable to provide a direction from the agent's current position for some reason.
    /// </summary>
    /// <param name="origin">The position from which to determine the fallback movement direction.</param>
    /// <param name="fallbackDirection">The resulting fallback movement direction, if available.</param>
    /// <returns>True if a valid fallback movement direction was obtained; otherwise, false.</returns>
    private bool TryGetStagedFallbackDirection(Vector3d origin, out Vector3d fallbackDirection)
    {
        fallbackDirection = Vector3d.Zero;
        int remainingStageAdvances = _stagedPlan.Steps.Length;
        if (!TryGetPreparedStage(origin, ref remainingStageAdvances, out HybridRouteStep currentStep))
            return false;

        if (currentStep.Kind == HybridRouteStepKind.Waypoint)
        {
            fallbackDirection = (currentStep.WaypointPosition - origin).Normalize();
            return true;
        }

        return TryGetOrCreateActiveStageGuide(currentStep, out IGuide activeGuide)
            && activeGuide.TryGetFallbackDirection(origin, out fallbackDirection);
    }

    /// <summary>
    /// Advances through already-completed stages with a bounded budget so malformed staged plans cannot
    /// recurse or spin indefinitely while trying to find the next actionable step.
    /// </summary>
    private bool TryGetPreparedStage(
        Vector3d origin,
        ref int remainingStageAdvances,
        out HybridRouteStep currentStep)
    {
        while (TryGetCurrentStage(out currentStep))
        {
            if (!IsStageTargetReached(origin, currentStep))
                return true;

            if (!TryAdvanceStage(ref remainingStageAdvances))
                break;
        }

        currentStep = null;
        return false;
    }

    /// <summary>
    /// Attempts to get the currently active stage from the staged plan based on the current stage index, and returns true if a valid stage is found; otherwise, false.
    /// </summary>
    /// <param name="currentStep">The currently active stage, if found.</param>
    /// <returns>True if a valid stage is found; otherwise, false.</returns>
    private bool TryGetCurrentStage(out HybridRouteStep currentStep)
    {
        currentStep = null;
        if (_stagedPlan == null
            || _stagedStepIndex < 0
            || _stagedStepIndex >= _stagedPlan.Steps.Length)
        {
            return false;
        }

        currentStep = _stagedPlan.Steps[_stagedStepIndex];
        return currentStep != null;
    }

    private bool TryAdvanceStage(ref int remainingStageAdvances)
    {
        if (remainingStageAdvances <= 0)
            return false;

        remainingStageAdvances--;
        AdvanceStage(dispose: false);
        return true;
    }

    /// <summary>
    /// Advances to the next stage in the staged plan, optionally disposing of the active stage guide if it implements IDisposable. This is called when the current stage's target has been reached, and it ensures that the guide progresses through the stages of the plan as the agent moves along the route. Disposing of the active stage guide when advancing can help free up resources associated with that guide if it is no longer needed after advancing to the next stage. However, in some cases we may want to keep the guide around (e.g. if it will be reused in a later stage), so this method allows for both options depending on whether disposal is desired when advancing.
    /// </summary>
    /// <param name="dispose">Indicates whether to dispose of the active stage guide when advancing.</param>
    private void AdvanceStage(bool dispose)
    {
        ReleaseActiveStageGuide(dispose);
        _stagedStepIndex++;
    }

    /// <summary>
    /// Releases the currently active stage guide, optionally disposing of it if it implements IDisposable. This is called when advancing to the next stage in the plan to ensure that any resources associated with the previous stage's guide are properly released, which can help prevent memory leaks and free up resources that are no longer needed after advancing to the next stage. Disposing of the active stage guide when releasing can help ensure that any unmanaged resources or other disposable resources associated with that guide are properly cleaned up, but in some cases we may want to keep the guide around (e.g. if it will be reused in a later stage), so this method allows for both options depending on whether disposal is desired when releasing the active stage guide.
    /// </summary>
    /// <param name="dispose">Indicates whether to dispose of the active stage guide when releasing.</param>
    private void ReleaseActiveStageGuide(bool dispose)
    {
        if (_activeStageGuide == null)
            return;

        PathGuideFactory.ReturnGuide(_activeStageGuide, dispose);
        _activeStageGuide = null;
        _activeStageGuideStepIndex = -1;
    }


    /// <summary>
    /// Attempts to get or create the active stage guide for the current stage in the staged plan, if the current stage is a path segment that requires a guide. This is used to ensure that we have an active guide available for providing directions when we are in a path segment stage of the plan, and it also handles creating the guide if it doesn't already exist for the current stage. If the current stage is not a path segment or if there is an issue creating the guide, this method returns false to indicate that no active stage guide is available for the current stage.
    /// </summary>
    /// <param name="currentStep">The current step in the staged plan.</param>
    /// <param name="guide">The active stage guide for the current step, if available.</param>
    /// <returns>True if an active stage guide is available or successfully created; otherwise, false.</returns>
    private bool TryGetOrCreateActiveStageGuide(HybridRouteStep currentStep, out IGuide guide)
    {
        guide = null;
        if (currentStep?.Kind != HybridRouteStepKind.PathSegment)
            return false;

        if (_activeStageGuide != null && _activeStageGuideStepIndex == _stagedStepIndex)
        {
            guide = _activeStageGuide;
            return true;
        }

        ReleaseActiveStageGuide(dispose: false);
        if (!PathGuideFactory.RequestGuide(currentStep.SegmentRequest, out _activeStageGuide)
            || _activeStageGuide == null)
        {
            return false;
        }

        _activeStageGuideStepIndex = _stagedStepIndex;
        guide = _activeStageGuide;
        return true;
    }

    /// <summary>
    /// Determines whether the target for the current stage has been reached based on the agent's position. This is used to check if we should advance to the next stage in the plan, and it helps ensure that the guide progresses through the stages of the plan as the agent moves along the route. The method checks the type of the current stage (e.g. waypoint or path segment) and determines if the agent's position is close enough to the stage's target position to be considered "reached", which can help prevent issues with precision or overshooting when determining if a stage has been completed.
    /// </summary>
    /// <param name="origin">The current position of the agent.</param>
    /// <param name="currentStep">The current step in the staged plan.</param>
    /// <returns>True if the target for the current stage has been reached; otherwise, false.</returns>
    private static bool IsStageTargetReached(Vector3d origin, HybridRouteStep currentStep)
    {
        // HybridRouteStep.Kind is factory-assigned to Waypoint or PathSegment only.
        Vector3d target = currentStep.Kind == HybridRouteStepKind.Waypoint
            ? currentStep.WaypointPosition
            : currentStep.SegmentRequest.TargetPosition;

        Fixed64 completionDistance = TrailblazerWorldManager.VoxelSize * Fixed64.Half;
        return (target - origin).SqrMagnitude <= completionDistance * completionDistance;
    }
}
