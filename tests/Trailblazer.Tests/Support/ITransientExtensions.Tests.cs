using FixedMathSharp;
using FluentAssertions;
using System;
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

    [Fact]
    public void SyncAndClearTransientState_ShouldBeNoOp_WhenTypeHasNoTransientProperties()
    {
        var instance = new NoTransientProps { Value = 5 };
        var other = new NoTransientProps { Value = 10 };

        ((ITransient)instance).SyncTransientState(other);
        instance.Value.Should().Be(5);

        ((ITransient)instance).ClearTransientState();
        instance.Value.Should().Be(5);
    }

    [Fact]
    public void ClearTransientState_ShouldUseStaticPropertyDefault_WhenAttributeSpecifiesPropertyMember()
    {
        var instance = new PropDefaultTransient { Direction = Vector3d.Up };
        instance.ClearTransientState();
        instance.Direction.Should().Be(Vector3d.Forward);
    }

    [Fact]
    public void SyncTransientState_ShouldThrowArgumentNullException_WhenOtherIsNull()
    {
        var target = new TestTransient();

        target.Invoking(static value => value.SyncTransientState(null!))
            .Should().Throw<ArgumentNullException>()
            .WithParameterName("other");
    }

    [Fact]
    public void SyncTransientState_ShouldThrowArgumentException_WhenTypesDoNotMatch()
    {
        var target = new TestTransient();
        var other = new NoTransientProps();

        target.Invoking(value => value.SyncTransientState(other))
            .Should().Throw<ArgumentException>()
            .WithParameterName("other")
            .WithMessage("*Type mismatch*");
    }

    private sealed class TestTransient : ITransient
    {
        [Transient]
        public int Count { get; set; }

        [Transient]
        public string? Label { get; set; }

        [Transient(typeof(Fixed4x4), nameof(Fixed4x4.Identity))]
        public Fixed4x4 Matrix { get; set; }

        [Transient(typeof(FixedQuaternion), nameof(FixedQuaternion.Identity))]
        public FixedQuaternion Rotation { get; set; }

        public int NonTransient { get; set; }
    }

    // No [Transient] properties — exercises the empty-delegate fast path in TransientStateUtility.
    private sealed class NoTransientProps : ITransient
    {
        public int Value { get; set; }
    }

    // Uses a static *property* (not a field) as the clear default — exercises the property-member
    // lookup path in TransientStateUtility.GetStaticMemberExpression.
    private sealed class PropDefaultTransient : ITransient
    {
        public static Vector3d ForwardDefault => Vector3d.Forward;

        [Transient(typeof(PropDefaultTransient), nameof(ForwardDefault))]
        public Vector3d Direction { get; set; }
    }
}
