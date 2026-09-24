using System;
using System.Reflection;
using Chronicler.Timing;
using FixedMathSharp;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DTimingTests
{
    [Theory]
    [InlineData(2147483646L, 2147483647L, uint.MaxValue)]
    [InlineData(4294967294L, 3155760000L, 0u)]
    public void WideClocksPreserveTheTwoActorBarrierAndExactMotion(
        long initialFrame, long seconds, uint fraction)
    {
        Vector3d[] control = RunBatch(64, new ChronicleTimestamp(2, 0));
        Vector3d[] late = RunBatch(initialFrame, new ChronicleTimestamp(seconds, fraction));
        Assert.Equal(control, late);
    }

    [Fact]
    public void ClockConfigurationAndResetRemainHostOwned()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorActor actor = fixture.CreateActor();
        fixture.Trailblazer.SetFrameRate(16);
        Assert.Equal(32, fixture.Gravitas.FrameRate);
        fixture.Gravitas.SetFrameRate(16);
        fixture.Step(actor);
        long physicsFrame = fixture.Gravitas.FrameCount;
        ChronicleTimestamp physicsTime = fixture.Gravitas.ElapsedTime;
        fixture.Trailblazer.Reset();
        Assert.Equal(physicsFrame, fixture.Gravitas.FrameCount);
        Assert.Equal(physicsTime, fixture.Gravitas.ElapsedTime);
        Assert.Equal(0, fixture.Trailblazer.FrameCount);
        Assert.Equal(ChronicleTimestamp.Zero, fixture.Trailblazer.ElapsedTime);
        Assert.Equal(16, fixture.Trailblazer.FrameRate);
        actor.Navigator.Reset();
    }

    private static Vector3d[] RunBatch(long initialFrame, ChronicleTimestamp initialTime)
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        SetTime(fixture.Trailblazer, initialFrame, initialTime);
        SetTime(fixture.Gravitas, initialFrame, initialTime);
        using GravitasNavigatorActor first = fixture.CreateActor();
        using GravitasNavigatorActor second = fixture.CreateActor(fixture.EndRoot);
        var trace = new Vector3d[12];
        ChronicleTimestamp expectedTime = initialTime;
        for (int i = 0; i < 3; i++)
        {
            if (i == 2)
            {
                fixture.Trailblazer.SetFrameRate(16);
                fixture.Gravitas.SetFrameRate(16);
            }
            first.Navigator.ApplyInputTrekRequest(Vector3d.Right, TrekRate.Fast);
            second.Navigator.ApplyInputTrekRequest(Vector3d.Left, TrekRate.Fast);
            fixture.Prepare(first, second);
            long frame = initialFrame + i + 1;
            expectedTime += new ChronicleDuration(0, i == 2 ? 0x10000000u : 0x08000000u);
            Assert.Equal(frame, fixture.Trailblazer.FrameCount);
            Assert.Equal(frame, fixture.Gravitas.FrameCount);
            Assert.Equal(expectedTime, fixture.Trailblazer.ElapsedTime);
            Assert.Equal(expectedTime, fixture.Gravitas.ElapsedTime);
            Assert.Equal(fixture.Trailblazer.DeltaTime, fixture.Gravitas.DeltaTime);
            fixture.Resolve(first, second);
            Assert.Equal(frame, fixture.Trailblazer.FrameCount);
            Assert.Equal(frame, fixture.Gravitas.FrameCount);
            Assert.Equal(first.Body.Position3d, first.Navigator.Position);
            Assert.Equal(second.Body.Position3d, second.Navigator.Position);
            Assert.Equal(frame, first.Navigator.LastCommittedCell!.Value.SimulationFrame);
            Assert.Equal(frame, second.Navigator.LastCommittedCell!.Value.SimulationFrame);
            Assert.False(first.Navigator.Motor!.TraversalInProgress);
            Assert.False(second.Navigator.Motor!.TraversalInProgress);
            Assert.Throws<InvalidOperationException>(first.Navigator.CommitFrameMotion);
            trace[i * 4] = first.Navigator.Position;
            trace[i * 4 + 1] = first.Navigator.Velocity;
            trace[i * 4 + 2] = second.Navigator.Position;
            trace[i * 4 + 3] = second.Navigator.Velocity;
        }
        Assert.NotEqual(fixture.StartRoot, first.Navigator.Position);
        return trace;
    }

    // Seed existing clocks near boundaries without adding runtime clock setters.
    private static void SetTime(object context, long frame, ChronicleTimestamp elapsed)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        object clock = context.GetType().GetField("_clock", fields)!.GetValue(context)!;
        var timeline = (ChronicleClock)clock.GetType().GetField("_timeline", fields)!.GetValue(clock)!;
        typeof(ChronicleClock).GetProperty(nameof(ChronicleClock.FrameCount))!.SetValue(timeline, frame);
        typeof(ChronicleClock).GetProperty(nameof(ChronicleClock.ElapsedTime))!.SetValue(timeline, elapsed);
    }
}
