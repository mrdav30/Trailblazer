using System.Linq;
using FluentAssertions;
using Gravitas;
using Trailblazer.Pathing;

namespace Trailblazer.Gravitas.Tests.Support;

internal static class GravitasPlanarMapFixture
{
    internal static void Publish(TrailblazerWorldContext navigation, GravitasWorldContext physics, NavigationMap map)
    {
        var operation = new NavigationMapCommitOperation(new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear, 1, navigation.FrameCount + 1);
        navigation.Pathing.Admit(operation).Should().BeTrue();
        for (int frame = 0; frame < 32 && operation.Receipt.Status == NavigationOperationStatus.Pending; frame++)
            Advance(navigation, physics);
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        for (int frame = 0; frame < 32 && !IsMaterialized(navigation, map.MapId); frame++)
            Advance(navigation, physics);
        IsMaterialized(navigation, map.MapId).Should().BeTrue();
    }

    private static bool IsMaterialized(TrailblazerWorldContext navigation, string mapId) =>
        navigation.Pathing.GetNavigationGraphDiagnostics().Maps.Single(map => map.MapId == mapId).IsMaterialized;

    private static void Advance(TrailblazerWorldContext navigation, GravitasWorldContext physics)
    {
        navigation.Simulate();
        physics.Simulate();
        physics.LateSimulate();
        navigation.LateSimulate();
    }
}
