using System;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationPagedSequenceTests
{
    [Fact]
    public void EmptyBuilder_ShouldSealToTheCanonicalZeroPageSequence()
    {
        var builder = new NavigationPagedSequence<int>.Builder(elementBytes: 4);

        NavigationPagedSequence<int> sequence = builder.Seal();

        sequence.Count.Should().Be(0);
        sequence.PersistentPageCount.Should().Be(0);
        sequence.RetainedBytes.Should().Be(0);
        sequence.GetEnumerator().MoveNext().Should().BeFalse();
    }

    [Fact]
    public void PageBoundary_ShouldPreserveOrderAndExactRetention()
    {
        var builder = new NavigationPagedSequence<int>.Builder(elementBytes: 4);
        for (int value = 0; value < 9; value++)
            builder.Append(value);

        builder.PersistentPageCount.Should().Be(5);
        builder.RetainedBytes.Should().Be(216);

        NavigationPagedSequence<int> sequence = builder.Seal();

        sequence.Count.Should().Be(9);
        sequence[0].Should().Be(0);
        sequence[7].Should().Be(7);
        sequence[8].Should().Be(8);
        sequence.PersistentPageCount.Should().Be(5);
        sequence.RetainedBytes.Should().Be(208);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void IndexOutsideSequence_ShouldReject(int index)
    {
        var builder = new NavigationPagedSequence<int>.Builder(elementBytes: 4);
        for (int value = 0; value < 9; value++)
            builder.Append(value);
        NavigationPagedSequence<int> sequence = builder.Seal();

        ((Action)(() => _ = sequence[index])).Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithParameterName("index");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveElementWidth_ShouldRejectInvalidRetentionAccounting(int elementBytes)
    {
        Action create = () => _ = new NavigationPagedSequence<int>.Builder(elementBytes);

        create.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(elementBytes));
    }
}
