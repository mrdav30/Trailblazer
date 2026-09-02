using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationPersistentCollectionTests
{
    [Fact]
    public void MediumSlots_ShouldKeepIndependentExactValuesAndReportMissingSlots()
    {
        NavigationMediumSlots<string> empty = default;
        empty.TryGet(TraversalMedium.Solid, out string? missing).Should().BeFalse();
        missing.Should().BeNull();

        NavigationMediumSlots<string> slots = empty
            .Set(TraversalMedium.Solid, "ground")
            .Set(TraversalMedium.Gas, "air")
            .Set(TraversalMedium.Liquid, "water");

        slots.TryGet(TraversalMedium.Solid, out string solid).Should().BeTrue();
        slots.TryGet(TraversalMedium.Gas, out string gas).Should().BeTrue();
        slots.TryGet(TraversalMedium.Liquid, out string liquid).Should().BeTrue();
        solid.Should().Be("ground");
        gas.Should().Be("air");
        liquid.Should().Be("water");
        empty.TryGet(TraversalMedium.Gas, out _).Should().BeFalse(
            "persistent slot updates must not mutate the prior snapshot");
    }

    [Fact]
    public void SurfaceComponentKeySet_ShouldPreserveSiblingMediaAndCanonicalOrder()
    {
        var firstAddress = new NavigationCellAddress("a", default);
        var secondAddress = new NavigationCellAddress("b", new VoxelIndex(1, 0, 0));
        var solid = new NavigationSurfaceComponentKey(firstAddress, TraversalMedium.Solid);
        var gas = new NavigationSurfaceComponentKey(firstAddress, TraversalMedium.Gas);
        var liquid = new NavigationSurfaceComponentKey(secondAddress, TraversalMedium.Liquid);
        NavigationSurfaceComponentKeySet source = NavigationSurfaceComponentKeySet.Empty
            .Add(liquid)
            .Add(gas)
            .Add(solid);

        source.Add(gas).Should().BeSameAs(source);
        source.Contains(solid).Should().BeTrue();
        source.Contains(gas).Should().BeTrue();
        source.Contains(liquid).Should().BeTrue();
        source.Contains(new NavigationSurfaceComponentKey(
            firstAddress,
            TraversalMedium.Liquid)).Should().BeFalse();
        source.Contains(new NavigationSurfaceComponentKey(
            new NavigationCellAddress("missing", default),
            TraversalMedium.Solid)).Should().BeFalse();
        source.Remove(new NavigationSurfaceComponentKey(
            firstAddress,
            TraversalMedium.Liquid)).Should().BeSameAs(source);
        source.Remove(new NavigationSurfaceComponentKey(
            new NavigationCellAddress("missing", default),
            TraversalMedium.Solid)).Should().BeSameAs(source);

        NavigationSurfaceComponentKeySet withoutSolid = source.Remove(solid);
        withoutSolid.Count.Should().Be(2);
        withoutSolid.Contains(solid).Should().BeFalse();
        withoutSolid.Contains(gas).Should().BeTrue(
            "removing one medium must retain sibling media at the same address");
        NavigationSurfaceComponentKeySet withoutFirstAddress = withoutSolid.Remove(gas);
        withoutFirstAddress.Count.Should().Be(1);
        withoutFirstAddress.Contains(liquid).Should().BeTrue();
        source.Count.Should().Be(3,
            "each removal must preserve the prior persistent snapshot");

        NavigationSurfaceComponentKeySet.Enumerator enumerator = source.GetEnumerator();
        var ordered = new NavigationSurfaceComponentKey[3];
        int count = 0;
        while (enumerator.MoveNext())
            ordered[count++] = enumerator.Current;
        count.Should().Be(3);
        ordered.Should().Equal(solid, gas, liquid);
        enumerator.MoveNext().Should().BeFalse();
    }

    [Fact]
    public void PersistentIdentitySets_ShouldDeduplicateAndReturnCanonicalCrossMapOrder()
    {
        var addressA = new NavigationCellAddress("a", new VoxelIndex(1, 0, 0));
        var addressB = new NavigationCellAddress("b", default);
        NavigationCellAddressSet addresses = NavigationCellAddressSet.Empty
            .Add(addressB)
            .Add(addressA);
        addresses.Add(addressA).Should().BeSameAs(addresses);
        addresses.Count.Should().Be(2);
        addresses.GetAt(0).Should().Be(addressA);
        addresses.GetAt(1).Should().Be(addressB);

        var ownerA = new NavigationConnectionOwnerKey("a", "z");
        var ownerB = new NavigationConnectionOwnerKey("b", "a");
        NavigationConnectionOwnerKeySet owners = NavigationConnectionOwnerKeySet.Empty
            .Add(ownerB)
            .Add(ownerA);
        owners.Add(ownerA).Should().BeSameAs(owners);
        owners.Count.Should().Be(2);
        owners.GetAt(0).Should().Be(ownerA);
        owners.GetAt(1).Should().Be(ownerB);
    }

    [Fact]
    public void DirectBakedLookup_ShouldRejectOutOfBindingAddressWithoutAliasing()
    {
        var configuration = new GridConfiguration(Vector3d.Zero, Vector3d.Zero);
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .Build();
        NavigationBakedCellLookup lookup = NavigationBakedCellLookup.Create(map);

        lookup.Kind.Should().Be(NavigationCellLookupKind.Direct);
        lookup.Find(default).Should().Be(0);
        lookup.Find(new VoxelIndex(-1, 0, 0)).Should().Be(-1);
    }

    [Fact]
    public void IntMapRightRotationRemoval_ShouldPreserveSnapshotsAndExactCopyAccounting()
    {
        PersistentIntMap<string> source = PersistentIntMap<string>.Empty
            .Set(3, "3")
            .Set(2, "2")
            .Set(4, "4")
            .Set(1, "1");

        PersistentIntMap<string> uncounted = source.Remove(4);
        PersistentIntMap<string> counted = source.Remove(4, out int copiedNodes);

        Values(source).Should().Equal("1", "2", "3", "4");
        Values(uncounted).Should().Equal("1", "2", "3");
        Values(counted).Should().Equal("1", "2", "3");
        copiedNodes.Should().Be(3,
            "one search-path node and the two right-rotation nodes are copied");
        counted.PersistentNodeCount.Should().Be(3);
        counted.RetainedBytes.Should().Be(248);
    }

    [Fact]
    public void IntMapRemovalVariants_ShouldPreserveSnapshotsAndChargeExactRotationCopies()
    {
        PersistentIntMap<string> singleChild = PersistentIntMap<string>.Empty
            .Set(2, "2")
            .Set(1, "1");

        singleChild.Remove(9).Should().BeSameAs(singleChild);
        singleChild.Remove(9, out int missingCopies).Should().BeSameAs(singleChild);
        missingCopies.Should().Be(0);
        singleChild.Remove(2).GetValueAt(0).Should().Be("1");
        PersistentIntMap<string> countedSingle = singleChild.Remove(2, out int singleCopies);
        Values(countedSingle).Should().Equal("1");
        singleCopies.Should().Be(0,
            "removing a root with one child can retain that child without path copying");
        Values(singleChild).Should().Equal("1", "2");

        PersistentIntMap<string> leftRight = PersistentIntMap<string>.Empty
            .Set(3, "3")
            .Set(1, "1")
            .Set(4, "4")
            .Set(2, "2");
        PersistentIntMap<string> uncountedLeftRight = leftRight.Remove(4);
        PersistentIntMap<string> leftRightResult = leftRight.Remove(4, out int leftRightCopies);
        Values(uncountedLeftRight).Should().Equal("1", "2", "3");
        Values(leftRightResult).Should().Equal("1", "2", "3");
        leftRightCopies.Should().Be(6,
            "the copied search path and both rotations have an exact bounded cost");

        PersistentIntMap<string> rightLeft = PersistentIntMap<string>.Empty
            .Set(1, "1")
            .Set(0, "0")
            .Set(3, "3")
            .Set(2, "2");
        PersistentIntMap<string> rightLeftResult = rightLeft.Remove(0, out int rightLeftCopies);
        Values(rightLeftResult).Should().Equal("1", "2", "3");
        rightLeftCopies.Should().Be(6,
            "the mirrored double rotation must have identical deterministic accounting");
        Values(rightLeft).Should().Equal("0", "1", "2", "3");
    }

    [Fact]
    public void SeamTreeTwoChildRemoval_ShouldUseCanonicalSuccessorAndPreserveSource()
    {
        var empty = new NavigationSeamEditTree<int, Box>(nodeBytes: 64);
        NavigationSeamEditTree<int, Box>.Editor seed = empty.Edit(
            NavigationSeamEditToken.Create());
        foreach (int key in new[] { 4, 2, 6, 1, 3, 5, 7 })
            seed.Set(key, new Box(key));
        NavigationSeamEditTree<int, Box> source = seed.Seal();

        NavigationSeamEditTree<int, Box>.Editor removal = source.Edit(
            NavigationSeamEditToken.Create());
        removal.Remove(4).Should().BeTrue();

        removal.OwnedNodeCount.Should().Be(2,
            "only the root and the successor search path require token-owned copies");
        removal.RetainedBytes.Should().Be(184);
        NavigationSeamEditTree<int, Box> result = removal.Seal();
        Values(source).Should().Equal(1, 2, 3, 4, 5, 6, 7);
        Values(result).Should().Equal(1, 2, 3, 5, 6, 7);
        result.Count.Should().Be(6);
        result.RetainedBytes.Should().Be(416);
    }

    [Fact]
    public void SeamTreeRemoval_ShouldPromoteAnOnlyRightChildWithoutMutatingSource()
    {
        NavigationSeamEditTree<int, Box> source = CreateSeamTree(1, 2);
        NavigationSeamEditTree<int, Box>.Editor editor = source.Edit(
            NavigationSeamEditToken.Create());

        editor.Remove(1).Should().BeTrue();
        NavigationSeamEditTree<int, Box> result = editor.Seal();

        Values(source).Should().Equal(1, 2);
        Values(result).Should().Equal(2);
        result.Count.Should().Be(1);
    }

    [Fact]
    public void SeamTreeRepeatedReference_ShouldReuseItsOwnedNode()
    {
        var empty = new NavigationSeamEditTree<int, Box>(nodeBytes: 64);
        NavigationSeamEditTree<int, Box>.Editor editor = empty.Edit(
            NavigationSeamEditToken.Create());
        var retained = new Box(7);
        editor.Set(7, retained);

        editor.Set(7, retained);

        editor.OwnedNodeCount.Should().Be(1);
        editor.Count.Should().Be(1);
        editor.Seal().TryGetValue(7, out Box value).Should().BeTrue();
        value.Should().BeSameAs(retained);
    }

    [Fact]
    public void SeamTreeCursorHeightOneBelowRequirement_ShouldRejectTraversal()
    {
        NavigationSeamEditTree<int, Box> tree = CreateSeamTree(2, 1);
        NavigationSeamEditTree<int, Box>.Cursor cursor = tree.CreateCursor(
            maximumHeight: 1,
            shellBytes: 56);

        Action begin = () => cursor.BeginAll(tree);

        begin.Should().Throw<InvalidOperationException>()
            .WithMessage("The configured seam tree height was exceeded.");
    }

    [Fact]
    public void SeamTreeCursorRestart_ShouldDiscardThePriorTraversalStack()
    {
        NavigationSeamEditTree<int, Box> tree = CreateSeamTree(4, 2, 6, 1, 3, 5, 7);
        NavigationSeamEditTree<int, Box>.Cursor cursor = tree.CreateCursor(
            maximumHeight: 4,
            shellBytes: 56);
        cursor.BeginAll(tree);

        cursor.BeginAtLeast(tree, minimum: 5);

        var values = new int[3];
        int count = 0;
        while (cursor.MoveNext())
            values[count++] = cursor.Current.Value;
        count.Should().Be(3);
        values.Should().Equal(5, 6, 7);
        cursor.Current.Should().BeNull();
        cursor.CurrentKey.Should().Be(0);
    }

    private static string[] Values(PersistentIntMap<string> map)
    {
        var values = new string[map.Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = map.GetValueAt(i);
        return values;
    }

    private static int[] Values(NavigationSeamEditTree<int, Box> tree)
    {
        NavigationSeamEditTree<int, Box>.Cursor cursor = tree.CreateCursor(
            maximumHeight: 8,
            shellBytes: 56);
        cursor.BeginAll(tree);
        var values = new int[tree.Count];
        int count = 0;
        while (cursor.MoveNext())
            values[count++] = cursor.Current.Value;
        count.Should().Be(values.Length);
        return values;
    }

    private static NavigationSeamEditTree<int, Box> CreateSeamTree(params int[] keys)
    {
        var empty = new NavigationSeamEditTree<int, Box>(nodeBytes: 64);
        NavigationSeamEditTree<int, Box>.Editor editor = empty.Edit(
            NavigationSeamEditToken.Create());
        foreach (int key in keys)
            editor.Set(key, new Box(key));
        return editor.Seal();
    }

    private sealed class Box
    {
        internal Box(int value) => Value = value;

        internal int Value { get; }
    }
}
