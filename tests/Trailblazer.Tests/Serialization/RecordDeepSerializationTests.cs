using FluentAssertions;
using Trailblazer.Serialization;
using Xunit;

namespace Trailblazer.Tests.Serialization;

public class RecordDeepSerializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Populate_ShouldApplyCanonicalDefaults_WhenStructDeepEntryIsMissing(bool useMemoryPack)
    {
        var source = new DeepStructContainer
        {
            State = new TestRecordableStruct { Count = 12, Enabled = false }
        };

        object payload = SerializeRecord(source, useMemoryPack);
        payload = RemovePayloadEntry(payload, useMemoryPack, "state");

        var target = new DeepStructContainer
        {
            State = new TestRecordableStruct { Count = 99, Enabled = false }
        };

        PopulateRecord(target, payload, useMemoryPack);

        target.State.Count.Should().Be(TestRecordableStruct.DefaultCount);
        target.State.Enabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestoreCanonicalDefaults_WhenStructDeepPayloadIsPresentButEmpty(bool useMemoryPack)
    {
        var source = new DeepStructContainer
        {
            State = new TestRecordableStruct
            {
                Count = TestRecordableStruct.DefaultCount,
                Enabled = true
            }
        };

        object payload = SerializeRecord(source, useMemoryPack);

        var target = new DeepStructContainer
        {
            State = new TestRecordableStruct { Count = 99, Enabled = false }
        };

        PopulateRecord(target, payload, useMemoryPack);

        target.State.Count.Should().Be(TestRecordableStruct.DefaultCount);
        target.State.Enabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Populate_ShouldClearNullableStruct_WhenNullableDeepEntryIsMissing(bool useMemoryPack)
    {
        var source = new DeepStructContainer
        {
            OptionalState = new TestRecordableStruct { Count = 12, Enabled = false }
        };

        object payload = SerializeRecord(source, useMemoryPack);
        payload = RemovePayloadEntry(payload, useMemoryPack, "optionalState");

        var target = new DeepStructContainer
        {
            OptionalState = new TestRecordableStruct { Count = 99, Enabled = false }
        };

        PopulateRecord(target, payload, useMemoryPack);

        target.OptionalState.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestoreNullableStruct_WhenPayloadEntryIsPresentEvenIfInnerValuesAreDefault(bool useMemoryPack)
    {
        var source = new DeepStructContainer
        {
            OptionalState = new TestRecordableStruct
            {
                Count = TestRecordableStruct.DefaultCount,
                Enabled = true
            }
        };

        object payload = SerializeRecord(source, useMemoryPack);

        var target = new DeepStructContainer();
        PopulateRecord(target, payload, useMemoryPack);

        target.OptionalState.Should().NotBeNull();
        target.OptionalState!.Value.Count.Should().Be(TestRecordableStruct.DefaultCount);
        target.OptionalState!.Value.Enabled.Should().BeTrue();
    }

    private static object SerializeRecord(IRecordable target, bool useMemoryPack)
    {
        return useMemoryPack
            ? MemoryPackRecordSerializer.Serialize(target)
            : JsonRecordSerializer.Serialize(target);
    }

    private static void PopulateRecord(IRecordable target, object payload, bool useMemoryPack)
    {
        if (useMemoryPack)
            MemoryPackRecordSerializer.Populate(target, (byte[])payload);
        else
            JsonRecordSerializer.Populate(target, (string)payload);
    }

    private static object RemovePayloadEntry(object payload, bool useMemoryPack, params string[] path)
    {
        return useMemoryPack
            ? SerializationPayloadEditor.RemoveMemoryPackEntry((byte[])payload, path)
            : SerializationPayloadEditor.RemoveJsonProperty((string)payload, path);
    }

    private sealed class DeepStructContainer : IRecordable
    {
        public TestRecordableStruct State;

        public TestRecordableStruct? OptionalState;

        public void RecordData(IChronicler chronicler)
        {
            RecordDeepStruct.Look(chronicler, ref State, "state");
            RecordNullableDeep.Look(chronicler, ref OptionalState, "optionalState");
        }
    }

    private struct TestRecordableStruct : IRecordable
    {
        public const int DefaultCount = 7;

        public int Count;

        public bool Enabled;

        public void RecordData(IChronicler chronicler)
        {
            chronicler.LookValue(ref Count, nameof(Count), DefaultCount);
            chronicler.LookValue(ref Enabled, nameof(Enabled), true);
        }
    }
}
