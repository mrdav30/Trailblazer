using System;
using System.IO;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

public sealed class ChartPrintExtensionsTests
{
    [Fact]
    public void TraversalAuthoringMap_PrintXZPlane_ShouldWriteTokens_AndValidateBounds()
    {
        TraversalAuthoringMap map = new(
            chartName: "PrintTokens",
            sourceMap: new string[,,]
            {
                {
                    { "S", "" },
                    { "L", "G" }
                }
            },
            minBounds: Vector3d.Zero,
            interval: Fixed64.One);

        string output = CaptureConsole(() => map.PrintXZPlane(0));

        output.Should().Contain("PrintTokens");
        output.Should().Contain("S ");
        output.Should().Contain("L ");
        output.Should().Contain("G ");

        Action outOfRange = () => map.PrintXZPlane(1);
        outOfRange.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NavigationChart_PrintHelpers_ShouldWriteWalkablePositions_AndPlaneLayout()
    {
        bool[,,] data = new bool[1, 2, 2]
        {
            {
                { true, false },
                { false, true }
            }
        };

        NavigationChart chart = NavigationChart.From3D("PrintChart", data, Vector3d.Zero, Fixed64.One);

        string walkableOutput = CaptureConsole(chart.PrintWalkablePositions);
        walkableOutput.Should().Contain("PrintChart");
        walkableOutput.Should().Contain("(0, 0, 0)");
        walkableOutput.Should().Contain("(1, 0, 1)");

        string planeOutput = CaptureConsole(() => chart.PrintXZPlane(0));
        planeOutput.Should().Contain("XZ Plane at Y=0 for Chart [PrintChart]");
        planeOutput.Should().Contain("O ");
        planeOutput.Should().Contain(". ");
    }

    private static string CaptureConsole(Action action)
    {
        TextWriter original = Console.Out;
        using StringWriter writer = new();

        try
        {
            Console.SetOut(writer);
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
