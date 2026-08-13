using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Grids;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Navigation;

/// <summary>
/// Route-shape counters returned by the mixed navigation scenario preflight helpers.
/// Benchmark methods return scalar values, but tests use this shape to verify the fixture still
/// contains the intended direct, guided, flow-field, and combined-steering work.
/// </summary>
public readonly struct NavigationScenarioSummary
{
    internal NavigationScenarioSummary(
        int agentsProcessed,
        int directLosAgents,
        int aStarAgents,
        int flowFieldAgents,
        int combinedSteeringAgents,
        int guideBackedAgents,
        int nonZeroHeadings)
    {
        AgentsProcessed = agentsProcessed;
        DirectLosAgents = directLosAgents;
        AStarAgents = aStarAgents;
        FlowFieldAgents = flowFieldAgents;
        CombinedSteeringAgents = combinedSteeringAgents;
        GuideBackedAgents = guideBackedAgents;
        NonZeroHeadings = nonZeroHeadings;
    }

    public int AgentsProcessed { get; }

    public int DirectLosAgents { get; }

    public int AStarAgents { get; }

    public int FlowFieldAgents { get; }

    public int CombinedSteeringAgents { get; }

    public int GuideBackedAgents { get; }

    public int NonZeroHeadings { get; }
}

/// <summary>
/// Runtime-like navigation benchmarks that batch many agents through one fixed-step style update.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Navigation", "Scenario")]
public class NavigationScenarioBenchmarks
{
    public const int MixedAgentCount100 = 100;
    public const int MixedAgentCount500 = 500;

    private const int DirectPlaneSize = 64;
    private const int GuidedPlaneSize = 96;
    private const int CombinedPlaneSize = 80;
    private const int CombinedOccupantCount = 512;

    private static readonly Vector3d DirectOffset = Vector3d.Zero;
    private static readonly Vector3d AStarOffset = new(80, 0, 0);
    private static readonly Vector3d FlowFieldOffset = new(200, 0, 0);
    private static readonly Vector3d CombinedOffset = new(0, 0, 120);
    private static readonly Vector3d MovementVelocity = new(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
    private static readonly Fixed64 MovingSpeed = (Fixed64)12;

    private BenchmarkPathFixture _fixture;
    private MixedSteeringScenario[] _mixed100;
    private MixedSteeringScenario[] _mixed500;
    private BenchmarkOccupant[] _combinedOccupants;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(BenchmarkChartFactory.GridConfigForArea(maxXExclusive: 320, maxZExclusive: 224));

        BenchmarkChartFactory.RegisterOpenPlane("ScenarioNavDirect64", DirectPlaneSize, DirectOffset);
        BenchmarkChartFactory.RegisterSparseBlockerField("ScenarioNavAStar96", GuidedPlaneSize, AStarOffset);
        BenchmarkChartFactory.RegisterSparseBlockerField("ScenarioNavFlow96", GuidedPlaneSize, FlowFieldOffset);
        BenchmarkChartFactory.RegisterOpenPlane("ScenarioNavCombined80", CombinedPlaneSize, CombinedOffset);

        _combinedOccupants = BenchmarkScenarioFactory.CreateOccupants(
            CombinedOccupantCount,
            width: 32,
            depth: 16,
            originX: (int)CombinedOffset.X + 8,
            originZ: (int)CombinedOffset.Z + 8);
        RegisterOccupants(_combinedOccupants);

        _mixed100 = CreateMixedScenarios(MixedAgentCount100);
        _mixed500 = CreateMixedScenarios(MixedAgentCount500);

        ValidateRepresentativeGuides(_mixed500);
        PrepareFirstFrameMixedSteering();
        MeasureFirstFrameMixedSteering100();
        MeasureFirstFrameMixedSteering500();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        RemoveOccupants(_combinedOccupants);
        _fixture?.Teardown();
    }

    [IterationSetup(Targets = new[]
    {
        nameof(FirstFrameMixedSteering_100Agents),
        nameof(FirstFrameMixedSteering_500Agents)
    })]
    public void PrepareFirstFrameMixedSteering()
    {
        _fixture.FlushGuideCache();
        ReapplyPathRequests(_mixed100);
        ReapplyPathRequests(_mixed500);
    }

    /// <summary>
    /// First navigation frame for a 100-agent mixed workload. Batches enough operations per
    /// invocation that first-frame setup can be compared without sub-millisecond benchmark noise.
    /// </summary>
    [Benchmark(OperationsPerInvoke = MixedAgentCount100)]
    [BenchmarkCategory("Navigation", "Scenario", "FirstFrame")]
    public int FirstFrameMixedSteering_100Agents()
    {
        return MeasureFirstFrameMixedSteering100().AgentsProcessed;
    }

    /// <summary>
    /// First navigation frame for a 500-agent mixed workload.
    /// </summary>
    [Benchmark(OperationsPerInvoke = MixedAgentCount500)]
    [BenchmarkCategory("Navigation", "Scenario", "FirstFrame")]
    public int FirstFrameMixedSteering_500Agents()
    {
        return MeasureFirstFrameMixedSteering500().AgentsProcessed;
    }

    /// <summary>
    /// Steady fixed-step navigation for a 100-agent mixed workload.
    /// </summary>
    [Benchmark(OperationsPerInvoke = MixedAgentCount100)]
    [BenchmarkCategory("Navigation", "Scenario", "FixedStep")]
    public int FixedStepMixedSteering_100Agents()
    {
        return MeasureFixedStepMixedSteering100().AgentsProcessed;
    }

    /// <summary>
    /// Steady fixed-step navigation for a 500-agent mixed workload.
    /// </summary>
    [Benchmark(OperationsPerInvoke = MixedAgentCount500)]
    [BenchmarkCategory("Navigation", "Scenario", "FixedStep")]
    public int FixedStepMixedSteering_500Agents()
    {
        return MeasureFixedStepMixedSteering500().AgentsProcessed;
    }

    public NavigationScenarioSummary MeasureFirstFrameMixedSteering100() => RunMixedSteering(_mixed100);

    public NavigationScenarioSummary MeasureFirstFrameMixedSteering500() => RunMixedSteering(_mixed500);

    public NavigationScenarioSummary MeasureFixedStepMixedSteering100() => RunMixedSteering(_mixed100);

    public NavigationScenarioSummary MeasureFixedStepMixedSteering500() => RunMixedSteering(_mixed500);

    private MixedSteeringScenario[] CreateMixedScenarios(int count)
    {
        int perKindCount = (count + 3) / 4;
        Vector3d[] directStarts = new Vector3d[perKindCount];
        Vector3d[] directDestinations = new Vector3d[perKindCount];
        Vector3d[] aStarStarts = new Vector3d[perKindCount];
        Vector3d[] flowStarts = new Vector3d[perKindCount];

        BenchmarkChartFactory.GenerateAdjacentRequestPairs(
            DirectPlaneSize,
            perKindCount,
            directStarts,
            directDestinations,
            DirectOffset);
        GenerateBorderStarts(AStarOffset, GuidedPlaneSize, perKindCount, aStarStarts);
        GenerateBorderStarts(FlowFieldOffset, GuidedPlaneSize, perKindCount, flowStarts);

        Vector3d aStarDestination = AStarOffset + new Vector3d(GuidedPlaneSize - 1, 0, GuidedPlaneSize - 1);
        Vector3d flowDestination = FlowFieldOffset + new Vector3d(GuidedPlaneSize - 1, 0, GuidedPlaneSize - 1);
        var scenarios = new MixedSteeringScenario[count];

        int directIndex = 0;
        int aStarIndex = 0;
        int flowIndex = 0;
        int combinedIndex = 0;

        for (int i = 0; i < count; i++)
        {
            MixedSteeringKind kind = (MixedSteeringKind)(i & 3);
            switch (kind)
            {
                case MixedSteeringKind.DirectLos:
                    {
                        Vector3d start = directStarts[directIndex];
                        Vector3d destination = directDestinations[directIndex++];
                        scenarios[i] = CreateGuidedScenario(kind, start, AStarPathRequest.Create(_fixture.Context, start, destination, Fixed64.One));
                        break;
                    }
                case MixedSteeringKind.AStar:
                    {
                        Vector3d start = aStarStarts[aStarIndex++];
                        scenarios[i] = CreateGuidedScenario(kind, start, AStarPathRequest.Create(_fixture.Context, start, aStarDestination, Fixed64.One));
                        break;
                    }
                case MixedSteeringKind.FlowField:
                    {
                        Vector3d start = flowStarts[flowIndex++];
                        scenarios[i] = CreateGuidedScenario(kind, start, FlowFieldPathRequest.Create(_fixture.Context, start, flowDestination, Fixed64.One));
                        break;
                    }
                default:
                    {
                        Vector3d start = CombinedOffset + new Vector3d(
                            10 + (combinedIndex % 28),
                            0,
                            10 + ((combinedIndex / 28) % 12));
                        BenchmarkSteerAgent agent = CreateMovingAgent(start);
                        scenarios[i] = new MixedSteeringScenario(kind, agent, NavSteering.CreateNew(_fixture.Context, agent.Radius), null);
                        combinedIndex++;
                        break;
                    }
            }
        }

        return scenarios;
    }

    private MixedSteeringScenario CreateGuidedScenario(
        MixedSteeringKind kind,
        Vector3d start,
        IPathRequest request)
    {
        if (request == null)
            throw new InvalidOperationException($"Preflight: could not create {kind} scenario request from {start}.");

        BenchmarkSteerAgent agent = CreateMovingAgent(start);
        NavSteering steering = NavSteering.CreateNew(_fixture.Context, agent.Radius);
        if (kind != MixedSteeringKind.DirectLos)
            steering.PathRecheckCooldownFrames = int.MaxValue;

        steering.ApplyPathRequest(request);
        return new MixedSteeringScenario(kind, agent, steering, request);
    }

    private static BenchmarkSteerAgent CreateMovingAgent(Vector3d position)
    {
        return new BenchmarkSteerAgent(position)
        {
            Speed = MovingSpeed,
            Velocity = MovementVelocity
        };
    }

    private static void ReapplyPathRequests(MixedSteeringScenario[] scenarios)
    {
        for (int i = 0; i < scenarios.Length; i++)
        {
            IPathRequest request = scenarios[i].Request;
            if (request != null)
                scenarios[i].Steering.ApplyPathRequest(request);
        }
    }

    private static NavigationScenarioSummary RunMixedSteering(MixedSteeringScenario[] scenarios)
    {
        int processed = 0;
        int direct = 0;
        int aStar = 0;
        int flow = 0;
        int combined = 0;
        int guided = 0;
        int nonZero = 0;

        for (int i = 0; i < scenarios.Length; i++)
        {
            MixedSteeringScenario scenario = scenarios[i];
            Vector3d heading;
            switch (scenario.Kind)
            {
                case MixedSteeringKind.DirectLos:
                    direct++;
                    heading = scenario.Steering.GetHeading(scenario.Agent);
                    break;
                case MixedSteeringKind.AStar:
                    aStar++;
                    heading = scenario.Steering.GetHeading(scenario.Agent);
                    break;
                case MixedSteeringKind.FlowField:
                    flow++;
                    heading = scenario.Steering.GetHeading(scenario.Agent);
                    break;
                default:
                    combined++;
                    heading = scenario.Steering.ComputeCombinedSteering(
                        scenario.Agent.Position,
                        scenario.Agent.Velocity,
                        scenario.Agent.Speed,
                        scenario.Agent.Radius,
                        scenario.Agent.GlobalId);
                    break;
            }

            if (scenario.Steering.TrailGuide != null)
                guided++;

            if (heading != Vector3d.Zero)
                nonZero++;

            processed++;
        }

        return new NavigationScenarioSummary(processed, direct, aStar, flow, combined, guided, nonZero);
    }

    private void ValidateRepresentativeGuides(MixedSteeringScenario[] scenarios)
    {
        bool directSeen = false;
        bool aStarSeen = false;
        bool flowSeen = false;

        for (int i = 0; i < scenarios.Length; i++)
        {
            MixedSteeringScenario scenario = scenarios[i];
            if (scenario.Kind == MixedSteeringKind.DirectLos && !directSeen)
            {
                EnsureAStarGuideResolves((AStarPathRequest)scenario.Request, nameof(MixedSteeringKind.DirectLos));
                directSeen = true;
            }
            else if (scenario.Kind == MixedSteeringKind.AStar && !aStarSeen)
            {
                EnsureAStarGuideResolves((AStarPathRequest)scenario.Request, nameof(MixedSteeringKind.AStar));
                aStarSeen = true;
            }
            else if (scenario.Kind == MixedSteeringKind.FlowField && !flowSeen)
            {
                EnsureFlowFieldGuideResolves((FlowFieldPathRequest)scenario.Request, nameof(MixedSteeringKind.FlowField));
                flowSeen = true;
            }

            if (directSeen && aStarSeen && flowSeen)
                break;
        }

        _fixture.FlushGuideCache();
    }

    private static void EnsureAStarGuideResolves(AStarPathRequest request, string requestName)
    {
        if (!request.Context.Guides.RequestGuide(request, out AStarGuide guide))
            throw new InvalidOperationException($"Preflight: {requestName} A* scenario guide failed to resolve.");

        request.Context.Guides.ReturnGuide(guide);
    }

    private static void EnsureFlowFieldGuideResolves(FlowFieldPathRequest request, string requestName)
    {
        if (!request.Context.Guides.RequestGuide(request, out FlowFieldGuide guide))
            throw new InvalidOperationException($"Preflight: {requestName} flow-field scenario guide failed to resolve.");

        request.Context.Guides.ReturnGuide(guide);
    }

    private static void GenerateBorderStarts(Vector3d origin, int size, int count, Vector3d[] starts)
    {
        int index = 0;
        for (int z = 0; z < size - 1 && index < count; z++)
            starts[index++] = origin + new Vector3d(0, 0, z);

        for (int x = 1; x < size - 1 && index < count; x++)
            starts[index++] = origin + new Vector3d(x, 0, 0);

        for (int z = 1; z < size && index < count; z++)
            starts[index++] = origin + new Vector3d(size - 1, 0, z);

        for (int x = size - 2; x > 0 && index < count; x--)
            starts[index++] = origin + new Vector3d(x, 0, size - 1);

        if (index < count)
            throw new InvalidOperationException($"Preflight: generated only {index} border starts for {count} requested agents.");
    }

    private void RegisterOccupants(BenchmarkOccupant[] occupants)
    {
        for (int i = 0; i < occupants.Length; i++)
        {
            BenchmarkOccupant occupant = occupants[i];
            if (_fixture.World.TryGetGrid(occupant.Position, out VoxelGrid grid))
                grid.TryAddVoxelOccupant(occupant);
        }
    }

    private void RemoveOccupants(BenchmarkOccupant[] occupants)
    {
        if (occupants == null)
            return;

        for (int i = 0; i < occupants.Length; i++)
        {
            BenchmarkOccupant occupant = occupants[i];
            if (_fixture.World.TryGetGrid(occupant.Position, out VoxelGrid grid))
                grid.TryRemoveVoxelOccupant(occupant);
        }
    }

    private enum MixedSteeringKind
    {
        DirectLos,
        AStar,
        FlowField,
        CombinedSteering
    }

    private readonly struct MixedSteeringScenario
    {
        internal MixedSteeringScenario(
            MixedSteeringKind kind,
            BenchmarkSteerAgent agent,
            NavSteering steering,
            IPathRequest request)
        {
            Kind = kind;
            Agent = agent;
            Steering = steering;
            Request = request;
        }

        internal MixedSteeringKind Kind { get; }

        internal BenchmarkSteerAgent Agent { get; }

        internal NavSteering Steering { get; }

        internal IPathRequest Request { get; }
    }
}
