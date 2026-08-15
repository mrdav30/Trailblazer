using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class PersistentMapRemovalTests
{
    [Fact]
    public void IntMapRemoval_ShouldPreserveThePriorRootAndOrderedSurvivors()
    {
        PersistentIntMap<string> source = PersistentIntMap<string>.Empty;
        for (int key = 1; key <= 7; key++)
            source = source.Set(key, key.ToString());

        PersistentIntMap<string> removed = source.Remove(4).Remove(1).Remove(7);
        PersistentIntMap<string> counted = source.Remove(4, out int copiedNodes);
        counted = counted.Remove(1, out _).Remove(7, out _);
        PersistentIntMap<string> absent = source.Remove(99, out int absentCopies);

        source.TryGetValue(4, out string original).Should().BeTrue();
        original.Should().Be("4");
        removed.TryGetValue(4, out _).Should().BeFalse();
        counted.TryGetValue(4, out _).Should().BeFalse();
        removed.Count.Should().Be(4);
        EnumerableValues(removed).Should().Equal("2", "3", "5", "6");
        EnumerableValues(counted).Should().Equal("2", "3", "5", "6");
        copiedNodes.Should().BeGreaterThan(0);
        copiedNodes.Should().BeLessThan(source.Count);
        absent.Should().BeSameAs(source);
        absentCopies.Should().Be(0);
    }

    [Fact]
    public void VoxelMapRemoval_ShouldPreserveThePriorRootAndOrderedSurvivors()
    {
        PersistentVoxelIndexMap<string> source = PersistentVoxelIndexMap<string>.Empty;
        for (int key = 1; key <= 7; key++)
            source = source.Set(Index(key), key.ToString());

        PersistentVoxelIndexMap<string> removed = source.Remove(Index(4), out bool didRemove);
        removed = removed.Remove(Index(1), out _).Remove(Index(7), out _);
        PersistentVoxelIndexMap<string> counted = source.Remove(
            Index(4),
            out bool didRemoveCounted,
            out int copiedNodes);
        counted = counted.Remove(Index(1), out _, out _).Remove(Index(7), out _, out _);
        PersistentVoxelIndexMap<string> absent = source.Remove(
            Index(99),
            out bool removedAbsent,
            out int absentCopies);

        didRemove.Should().BeTrue();
        didRemoveCounted.Should().BeTrue();
        source.TryGetValue(Index(4), out string original).Should().BeTrue();
        original.Should().Be("4");
        removed.TryGetValue(Index(4), out _).Should().BeFalse();
        counted.TryGetValue(Index(4), out _).Should().BeFalse();
        removed.Count.Should().Be(4);
        VoxelKeys(removed).Should().Equal(Index(2), Index(3), Index(5), Index(6));
        VoxelKeys(counted).Should().Equal(Index(2), Index(3), Index(5), Index(6));
        copiedNodes.Should().BeGreaterThan(0);
        copiedNodes.Should().BeLessThan(source.Count);
        removedAbsent.Should().BeFalse();
        absent.Should().BeSameAs(source);
        absentCopies.Should().Be(0);
    }

    [Fact]
    public void GridConfigurationMapRemoval_ShouldPreserveThePriorRootAndOrderedSurvivors()
    {
        PersistentGridConfigurationMap<string> source = PersistentGridConfigurationMap<string>.Empty;
        for (int key = 1; key <= 7; key++)
            source = source.Set(ConfigurationKey(key), key.ToString());

        PersistentGridConfigurationMap<string> removed = source
            .Remove(ConfigurationKey(4))
            .Remove(ConfigurationKey(1))
            .Remove(ConfigurationKey(7));
        PersistentGridConfigurationMap<string> counted = source.Remove(
            ConfigurationKey(4),
            out bool didRemove,
            out int copiedNodes);
        counted = counted.Remove(ConfigurationKey(1), out _, out _)
            .Remove(ConfigurationKey(7), out _, out _);
        PersistentGridConfigurationMap<string> absent = source.Remove(
            ConfigurationKey(99),
            out bool removedAbsent,
            out int absentCopies);

        source.TryGetValue(ConfigurationKey(4), out string original).Should().BeTrue();
        original.Should().Be("4");
        removed.TryGetValue(ConfigurationKey(4), out _).Should().BeFalse();
        didRemove.Should().BeTrue();
        counted.TryGetValue(ConfigurationKey(4), out _).Should().BeFalse();
        removed.Count.Should().Be(4);
        GridValues(removed).Should().Equal("2", "3", "5", "6");
        GridValues(counted).Should().Equal("2", "3", "5", "6");
        copiedNodes.Should().BeGreaterThan(0);
        copiedNodes.Should().BeLessThan(source.Count);
        removedAbsent.Should().BeFalse();
        absent.Should().BeSameAs(source);
        absentCopies.Should().Be(0);
    }

    private static string[] EnumerableValues(PersistentIntMap<string> map)
    {
        var values = new string[map.Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = map.GetValueAt(i);
        return values;
    }

    private static VoxelIndex[] VoxelKeys(PersistentVoxelIndexMap<string> map)
    {
        var keys = new VoxelIndex[map.Count];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = map.GetKeyAt(i);
        return keys;
    }

    private static string[] GridValues(PersistentGridConfigurationMap<string> map)
    {
        var values = new string[map.Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = map.GetValueAt(i);
        return values;
    }

    private static VoxelIndex Index(int key) => new(key, 0, 0);

    private static GridConfigurationKey ConfigurationKey(int key)
    {
        var position = new Vector3d((Fixed64)key, Fixed64.Zero, Fixed64.Zero);
        var configuration = new GridConfiguration(position, position);
        configuration.TryNormalize(out NormalizedGridConfiguration normalized)
            .Should().BeTrue();
        return normalized.Key;
    }
}
