# **Trailblazer Pathing System Overview**

---

## 1. Defining the World: NavigationChart & PathManager

### 1.1 NavigationChart

* Represents a 3D grid of walkable or blocked cells.
* Instantiated via `NavigationChart.From3D(bool[,,] map)`.
* `IsWalkable(Vector3d worldPos)` determines if a world position lies in a valid walkable cell.
* `GetWalkablePositions()` iterates through world-space positions of all walkable cells.

### 1.2 PathManager

* Central registry for all loaded `NavigationChart` instances.
* Provides:

  * `GetValidPathRequest(...)` to snap arbitrary start/end to closest walkable nodes.
  * `NeedsPath(...)` to determine if straight-line travel is viable for a given `unitSize`.
  * Handles initialization and unloading of charts.
  * Internally manages deferred unloading when charts are still in use (via caching).

---

## 2. Surveyors: Raw Pathfinding Engines

### 2.1 AStarSurveyor

* Implements heap-based A\* using `PathPartition` nodes.
* Tracks `PathCost`, heuristic values, and backpointers.
* Supports `PathPartition.PathCostModifier` for dynamic terrain penalties.
* `FindPath(...)` returns a set of waypoints, optionally smoothed with direction filtering and/or Catmull-Rom spline.
* Performs edge-leg validation to prevent diagonal corner-cutting for arbitrary unit sizes.

### 2.2 FlowFieldSurveyor

* Executes a reverse breadth-first flood starting from the goal.
* Each partition records `PathCost` as a distance metric (not true cost).
* `GenerateFlowFields()` computes a flow direction per node, pointing to the neighbor with the lowest cost, respecting leg and unit size constraints.
* `PathPartition.PathCostModifier` is applied during flow direction selection (not flooding).
* Supports full 3D movement (via Y-axis offsets and leg validation).
* Agents use `SampleFlowVector(...)` for bilinear interpolation to generate smooth directional movement.

---

## 3. Guide Caching: ReusableSurveyResultCache & PathGuideFactory

### 3.1 ReusableSurveyResultCache

* Caches up to 128 results keyed by a deterministic hash of path request.
* Results are pooled and reused when not marked `IsInUse`.
* Old results are evicted via `CullExpired(...)`, and invalidated when associated charts are unloaded.

### 3.2 PathGuideFactory

* Primary entry point: `RequestGuide(Vector3d origin, Vector3d destination, IPathRequest request)`
* Internally:

  1. Snaps origin/destination to valid nodes.
  2. Sets `request.Start` and `request.End`.
  3. Chooses the appropriate surveyor (AStar or FlowField) based on request type.
  4. Caches or computes a new result via `ReusableSurveyResultCache`.
  5. Returns a guide (`AStarGuide` or `FlowFieldGuide`) with `MarkInUse()` called.
* Supports `ReturnGuide(...)` to release or evict used guides.
* Automatically invalidates all cached paths referencing an unloaded chart.

---

## 4. IGuide, AStarGuide, FlowFieldGuide

All guides implement:

```csharp
public interface IGuide {
    bool TryGetMovementDirection(Vector3d origin, out Vector3d direction);
    bool TryGetFallbackDirection(Vector3d from, out Vector3d fallbackDirection);
}
```

### AStarGuide

* Implements `IWaypointGuide`, which includes:

```csharp
public interface IWaypointGuide : IGuide {
    int CurrentWaypointIndex { get; }
    int GetIndex(Vector3d from);
    void AdvanceWaypoint();
    Vector3d GetMovementDirection(Vector3d from);
}
```

* Stores waypoint vector positions (`AStarWaypoint[]`).
* `GetMovementDirection()` returns the normalized direction to the current waypoint
* `HasArrived()` when at final waypoint.

### FlowFieldGuide

* Stores directional vector field (`SwiftDictionary<int, FlowField>`).
* `TryGetMovementDirection()` samples field using interpolation.
* `HasArrived()` when current field has `IsGoal == true`.

---

## 5. NavSteering: Runtime Movement Logic

### 5.1 Fields

* `IGuide TrailGuide`
* `IPathRequest CurrentRequest`
* `Vector3d Destination`
* `int CurrentIndex`
* Flags: `IsFollowingTrail`, `IsAtDestination`, `IsStuck`, `HasLineOfSightPath`, etc.

### 5.2 ApplyPathRequest(...)

* Validates destination node.
* Generates a guide (via `PathGuideFactory`).
* Sets state flags accordingly.

### 5.3 OnSimulate(INavigate body)

* Runs every frame to:

  * Check line of sight.
  * Request guide if needed.
  * Get directional vector (waypoint-based or flow field).
  * Blend avoidance/group-steering.
  * Call NavMotor with resulting vector.
  * Call `Arrive()` if close or stuck.

### 5.4 Arrive() / StopMove()

* Flags agent as completed.
* Returns guide to cache.
* Fires OnArrive event.

---

## 6. How You’d Use This in Your Game Loop

```csharp
var navigator = new MyConcreteNavigator(); // extends Navigator / INavigate
navigator.Setup(startingPosition, startingRotation, initialVelocity, gridSize);
navigator.Initialize(initialSurfaceState); // sets up its NavSteering & NavMotor

PathingManager.Register(myNavigationChart); // register any maps before pathfinding
PathingManager.InitializeMap(myNavigationChart.Name);

Vector3d target = new Vector3d(10, 0, 15);  // request a path
var request = AStarPathRequest.CreateEmpty();
request.Heuristic = HeuristicMethod.Manhattan;
request.AllowUnwalkable = false;
navigator.ApplyGuidedTravelRequest(target, request);

// Each frame:
navigator.Simulate();
navigator.CommitFrameMotion();
```

Under the hood, `Simulate()` calls `NavSteering.OnSimulate(...)` → computes a direction → raises `OnStartTraversal(...)`, which NavMotor consumes to apply movement. `CommitFrameMotion()` then updates actual position, velocity, acceleration.

---

## Why All These Layers?

* **NavigationChart & PathManager** let you manage multiple 3D navigation zones (charts) at runtime.
* **Surveyors** (A\* or FlowField) remain engine-agnostic, giving precise vs gradient pathing options.
* **ReusableSurveyResultCache** avoids redundant path computations for agents with identical path needs.
* **PathGuideFactory** abstracts caching, reuse, invalidation, and result freshness.
* **NavSteering** turns path data into runtime steering while remaining decoupled from physics.

Trailblazer combines flexible 3D pathfinding, runtime caching, and extensible guide logic for deterministic, lockstep-safe navigation—ideal for real-time simulations, games, and AI swarms alike.

---

## 7. Tips & Gotchas

### 7.1 Line-of-Sight Shortcut

* Use `PathManager.NeedsPath(start, end, unitSize)` before requesting a path.
* If false, a direct straight-line move is viable — saves computation.

### 7.2 Guide Lifetime

* Always call `PathGuideFactory.ReturnGuide(guide)` when done.
* If you skip this, the result remains marked `IsInUse` and never returns to the cache.

### 7.3 Spline Smoothing

* Enable `UseSplineSmoothing = true` on `AStarPathRequest` to generate curved paths.
* Uses Catmull-Rom interpolation on reduced waypoint set (direction changes only).

### 7.4 FlowField Edge Handling

* FlowFieldSurveyor includes leg-based edge checks.
* Diagonal neighbors are rejected unless both orthogonal legs are walkable for the unit.

### 7.5 PathCostModifier

* `PathPartition.PathCostModifier` lets you influence routing without altering the grid.
* Used during A\* scoring and FlowField vector generation (but not flooding).

### 7.6 Cached Path Invalidation

* When a chart is unloaded, all cached paths using that chart are marked invalid.
* They’ll be skipped or rebuilt when accessed again.

### 7.7 Parallel Test Failures

* Ensure any shared state (e.g. global maps, Voxel partition providers) is synchronized.
* Use `ReaderWriterLockSlim` or defer teardown logic across frames if needed.
