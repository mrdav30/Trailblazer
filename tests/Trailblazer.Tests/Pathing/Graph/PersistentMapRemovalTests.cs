using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Topology;
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
    public void IntMapRepeatedValueAndMissingLeftRemoval_ShouldAvoidPersistentCopies()
    {
        string retained = new('x', 1);
        PersistentIntMap<string> source = PersistentIntMap<string>.Empty
            .Set(2, retained)
            .Set(3, "right");

        PersistentIntMap<string> uncounted = source.Set(2, retained);
        PersistentIntMap<string> counted = source.Set(2, retained, out int setCopies);
        PersistentIntMap<string> missing = source.Remove(-1);
        PersistentIntMap<string> countedMissing = source.Remove(-1, out int removeCopies);

        uncounted.GetValueAt(0).Should().BeSameAs(retained);
        counted.GetValueAt(0).Should().BeSameAs(retained);
        setCopies.Should().Be(0,
            "retaining the same immutable value must reuse its complete persistent path");
        missing.Should().BeSameAs(source);
        countedMissing.Should().BeSameAs(source);
        removeCopies.Should().Be(0);
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
        PersistentVoxelIndexMap<string> uncountedAbsent = source.Remove(
            Index(99),
            out bool removedUncountedAbsent);

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
        removedUncountedAbsent.Should().BeFalse();
        uncountedAbsent.Should().BeSameAs(source);
    }

    [Fact]
    public void VoxelMapMissingLeftRemoval_ShouldPreserveRootWithoutCopies()
    {
        PersistentVoxelIndexMap<string> source = PersistentVoxelIndexMap<string>.Empty
            .Set(Index(2), "two")
            .Set(Index(3), "three");

        PersistentVoxelIndexMap<string> uncounted = source.Remove(
            Index(-1),
            out bool removed);
        PersistentVoxelIndexMap<string> counted = source.Remove(
            Index(-1),
            out bool countedRemoved,
            out int copies);

        removed.Should().BeFalse();
        countedRemoved.Should().BeFalse();
        uncounted.Should().BeSameAs(source);
        counted.Should().BeSameAs(source);
        copies.Should().Be(0);
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

    [Fact]
    public void GridConfigurationMapReplacementAndMissingRemoval_ShouldPreserveCountAndOrder()
    {
        PersistentGridConfigurationMap<string> source = PersistentGridConfigurationMap<string>.Empty;
        for (int key = 1; key <= 7; key++)
            source = source.Set(ConfigurationKey(key), key.ToString());

        PersistentGridConfigurationMap<string> replacement = source.Set(
            ConfigurationKey(4),
            "replacement");
        replacement.Count.Should().Be(7);
        replacement.TryGetValue(ConfigurationKey(4), out string replacementValue)
            .Should().BeTrue();
        replacementValue.Should().Be("replacement");

        PersistentGridConfigurationMap<string> uncountedAbsent =
            source.Remove(ConfigurationKey(99));
        GridValues(uncountedAbsent).Should().Equal("1", "2", "3", "4", "5", "6", "7");
    }

    [Fact]
    public void GridConfigurationMapMissingLeftRemoval_ShouldPreserveRootWithoutCopies()
    {
        PersistentGridConfigurationMap<string> source =
            PersistentGridConfigurationMap<string>.Empty
                .Set(ConfigurationKey(2), "two")
                .Set(ConfigurationKey(3), "three");

        PersistentGridConfigurationMap<string> uncounted = source.Remove(ConfigurationKey(-1));
        PersistentGridConfigurationMap<string> counted = source.Remove(
            ConfigurationKey(-1),
            out bool removed,
            out int copies);

        GridValues(uncounted).Should().Equal("two", "three");
        counted.Should().BeSameAs(source);
        removed.Should().BeFalse();
        copies.Should().Be(0);
    }

    [Fact]
    public void GridConfigurationMap_ShouldOrderAllStructuralConfigurationDimensions()
    {
        GridConfigurationKey[] keys =
        {
            ConfigurationKey(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2),
                GridTopologyKind.RectangularPrism,
                GridTopologyMetrics.Rectangular((Fixed64)1, (Fixed64)1, (Fixed64)1)),
            ConfigurationKey(new Vector3d(0, 0, 0), new Vector3d(2, 2, 3),
                GridTopologyKind.RectangularPrism,
                GridTopologyMetrics.Rectangular((Fixed64)1, (Fixed64)1, (Fixed64)1)),
            ConfigurationKey(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2),
                GridTopologyKind.HexPrism,
                GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop)),
            ConfigurationKey(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2),
                GridTopologyKind.RectangularPrism,
                GridTopologyMetrics.Rectangular((Fixed64)2, (Fixed64)1, (Fixed64)1)),
            ConfigurationKey(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2),
                GridTopologyKind.RectangularPrism,
                GridTopologyMetrics.Rectangular((Fixed64)1, (Fixed64)2, (Fixed64)1)),
            ConfigurationKey(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2),
                GridTopologyKind.RectangularPrism,
                GridTopologyMetrics.Rectangular((Fixed64)1, (Fixed64)1, (Fixed64)2))
        };
        PersistentGridConfigurationMap<string> map = PersistentGridConfigurationMap<string>.Empty;
        for (int i = keys.Length - 1; i >= 0; i--)
            map = map.Set(keys[i], i.ToString());

        map.Count.Should().Be(keys.Length,
            "bounds, topology, and each metric are independent configuration identities");
        for (int i = 0; i < keys.Length; i++)
        {
            map.TryGetValue(keys[i], out string value).Should().BeTrue();
            value.Should().Be(i.ToString());
        }


        Vector3d point = Vector3d.Zero;
        GridConfigurationKey rectangularPoint = ConfigurationKey(
            point,
            point,
            GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular(Fixed64.One));
        GridConfigurationKey hexPoint = ConfigurationKey(
            point,
            point,
            GridTopologyKind.HexPrism,
            GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop));
        GridConfigurationKey widerHexPoint = ConfigurationKey(
            point,
            point,
            GridTopologyKind.HexPrism,
            GridTopologyMetrics.Hex((Fixed64)2, Fixed64.One, HexOrientation.PointyTop));
        PersistentGridConfigurationMap<string> topologyMap =
            PersistentGridConfigurationMap<string>.Empty
                .Set(rectangularPoint, "rectangular")
                .Set(hexPoint, "hex")
                .Set(widerHexPoint, "wide-hex");
        topologyMap.TryGetValue(rectangularPoint, out _).Should().BeTrue();
        topologyMap.TryGetValue(hexPoint, out _).Should().BeTrue();
        topologyMap.TryGetValue(widerHexPoint, out _).Should().BeTrue();

        PersistentGridConfigurationMap<string> singleRotation =
            PersistentGridConfigurationMap<string>.Empty
                .Set(ConfigurationKey(3), "three")
                .Set(ConfigurationKey(2), "two")
                .Set(ConfigurationKey(1), "one");
        GridValues(singleRotation).Should().Equal("one", "two", "three");
    }

    [Fact]
    public void GridConfigurationMapRemovalRotations_ShouldPreserveCanonicalOrder()
    {

        PersistentGridConfigurationMap<string> leftRight =
            PersistentGridConfigurationMap<string>.Empty
                .Set(ConfigurationKey(3), "3")
                .Set(ConfigurationKey(1), "1")
                .Set(ConfigurationKey(4), "4")
                .Set(ConfigurationKey(2), "2")
                .Remove(ConfigurationKey(4));
        GridValues(leftRight).Should().Equal("1", "2", "3");

        PersistentGridConfigurationMap<string> singleLeft =
            PersistentGridConfigurationMap<string>.Empty
                .Set(ConfigurationKey(2), "2")
                .Set(ConfigurationKey(1), "1")
                .Remove(ConfigurationKey(2));
        GridValues(singleLeft).Should().Equal("1");
    }

    [Fact]
    public void GridConfigurationMapOrdinalOutsideCount_ShouldReject()
    {
        PersistentGridConfigurationMap<string> source =
            PersistentGridConfigurationMap<string>.Empty.Set(ConfigurationKey(1), "1");

        ((System.Action)(() => _ = source.GetValueAt(-1))).Should()
            .Throw<System.ArgumentOutOfRangeException>()
            .WithParameterName("ordinal");
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

    private static GridConfigurationKey ConfigurationKey(
        Vector3d boundsMin,
        Vector3d boundsMax,
        GridTopologyKind topology,
        GridTopologyMetrics metrics)
    {
        var configuration = new GridConfiguration(
            boundsMin,
            boundsMax,
            topologyKind: topology,
            topologyMetrics: metrics);
        configuration.TryNormalize(out NormalizedGridConfiguration normalized)
            .Should().BeTrue();
        return normalized.Key;
    }
}
