using System;
using FluentAssertions;
using Xunit;

namespace Trailblazer.Tests;

public class AllocationTestUtilityTests
{
    [Fact]
    public void MeasureAllocatedBytes_ShouldReportZeroForNonAllocatingWork()
    {
        GC.GetConfigurationVariables()["ConcurrentGC"].Should().Be(false,
            "the test host must disable background GC for reliable allocation counters");
        int calls = 0;
        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() => calls++);

        allocated.Should().Be(0);
        calls.Should().Be(1);
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

    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    public void MeasureAllocatedBytes_ShouldPreserveCountsAcrossBlockingCollection(int allocationSize)
    {
        byte[]? retained = null;
        int calls = 0;
        int collectionsBefore = GC.CollectionCount(0);
        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            calls++;
            if (allocationSize > 0)
                retained = new byte[allocationSize];
            GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        });

        calls.Should().Be(1);
        GC.CollectionCount(0).Should().BeGreaterThan(collectionsBefore);
        if (allocationSize == 0)
            allocated.Should().Be(0);
        else
        {
            retained.Should().HaveCount(allocationSize);
            allocated.Should().BeGreaterThanOrEqualTo(allocationSize);
        }
        GC.KeepAlive(retained);
    }

    [Fact]
    public void MeasureAllocatedBytes_ShouldPropagateWorkExceptionWithoutRetry()
    {
        var expected = new InvalidOperationException("Measured work failed.");
        int calls = 0;

        Exception? actual = Record.Exception(() =>
            AllocationTestUtility.MeasureAllocatedBytes(() =>
            {
                calls++;
                GC.Collect(0, GCCollectionMode.Forced, blocking: true);
                throw expected;
            }));

        actual.Should().BeSameAs(expected);
        calls.Should().Be(1);
    }
}
