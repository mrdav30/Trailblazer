using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Support;
using Xunit;

namespace Trailblazer.Tests.Support;

public sealed class ITransientExtensionsTests
{
    [Fact]
    public void SyncTransientState_Extension_ShouldCopyTransientProperties_WithoutTouchingNonTransientState()
    {
        var source = new TestTransient
        {
            Count = 7,
            Label = "synced",
            Matrix = new Fixed4x4(),
            Rotation = FixedQuaternion.FromAxisAngle(Vector3d.Right, Fixed64.Half),
            NonTransient = 99
        };

        var target = new TestTransient
        {
            Count = 1,
            Label = "old",
            Matrix = Fixed4x4.Identity,
            Rotation = FixedQuaternion.Identity,
            NonTransient = 5
        };

        ITransientExtensions.SyncTransientState(target, source);

        target.Count.Should().Be(7);
        target.Label.Should().Be("synced");
        target.Matrix.Should().Be(source.Matrix);
        target.Rotation.Should().Be(source.Rotation);
        target.NonTransient.Should().Be(5);
    }

    [Fact]
    public void ClearTransientState_Extension_ShouldResetTransientProperties_ToDefaultValues()
    {
        var target = new TestTransient
        {
            Count = 4,
            Label = "clear me",
            Matrix = new Fixed4x4(),
            Rotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.Half),
            NonTransient = 42
        };

        ITransientExtensions.ClearTransientState(target);

        target.Count.Should().Be(0);
        target.Label.Should().BeNull();
        target.Matrix.Should().Be(Fixed4x4.Identity);
        target.Rotation.Should().Be(FixedQuaternion.Identity);
        target.NonTransient.Should().Be(42);
    }

    private sealed class TestTransient : ITransient
    {
        [Transient]
        public int Count { get; set; }

        [Transient]
        public string? Label { get; set; }

        [Transient]
        public Fixed4x4 Matrix { get; set; }

        [Transient]
        public FixedQuaternion Rotation { get; set; }

        public int NonTransient { get; set; }
    }
}
