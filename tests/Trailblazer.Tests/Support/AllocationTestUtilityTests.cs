using System;
using System.Runtime;
using FluentAssertions;
using Xunit;

namespace Trailblazer.Tests;

public class AllocationTestUtilityTests
{
    [Fact]
    public void MeasureAllocatedBytes_ShouldReportZeroForNonAllocatingWork()
    {
        GCLatencyMode measuredMode = default;
        long allocated = AllocationTestUtility.MeasureAllocatedBytes(
            () => measuredMode = GCSettings.LatencyMode);

        allocated.Should().Be(0);
        measuredMode.Should().Be(GCLatencyMode.NoGCRegion);
    }

    [Fact]
    public void MeasureAllocatedBytes_ShouldCountRealAllocationsAndInvokeWorkOnce()
    {
        int calls = 0;
        byte[]? retained = null;

        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            calls++;
            retained = new byte[1024];
        });

        calls.Should().Be(1);
        retained.Should().HaveCount(1024);
        allocated.Should().BeGreaterThanOrEqualTo(1024);
        GC.KeepAlive(retained);
    }

    [Fact]
    public void MeasureAllocatedBytes_ShouldRejectACollectionDuringMeasurement()
    {
        Action measure = () => AllocationTestUtility.MeasureAllocatedBytes(
            static () => GC.Collect(0, GCCollectionMode.Forced, blocking: true));

        measure.Should().Throw<InvalidOperationException>();
        GCSettings.LatencyMode.Should().NotBe(GCLatencyMode.NoGCRegion);
    }

    [Fact]
    public void MeasureAllocatedBytes_ShouldReleaseRegionWhenWorkThrows()
    {
        var expected = new InvalidOperationException("Measured work failed.");

        Exception? actual = Record.Exception(() =>
            AllocationTestUtility.MeasureAllocatedBytes(() => throw expected));

        actual.Should().BeSameAs(expected);
        GCSettings.LatencyMode.Should().NotBe(GCLatencyMode.NoGCRegion);
        AllocationTestUtility.MeasureAllocatedBytes(static () => { }).Should().Be(0);
    }

    [Fact]
    public void MeasureAllocatedBytes_ShouldRejectNestingWithoutEndingTheOuterRegion()
    {
        Action work = static () => { };
        Action measure = () => AllocationTestUtility.MeasureAllocatedBytes(work);
        Exception? exception = null;
        bool outerRegionStillActive = false;

        AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            exception = Record.Exception(measure);
            outerRegionStillActive = GCSettings.LatencyMode == GCLatencyMode.NoGCRegion;
        });

        exception.Should().BeOfType<InvalidOperationException>();
        outerRegionStillActive.Should().BeTrue();
    }
}
