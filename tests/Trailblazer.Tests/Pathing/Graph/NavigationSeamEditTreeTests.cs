//=======================================================================
// NavigationSeamEditTreeTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationSeamEditTreeTests
{
    [Fact]
    public void LayoutConstants_ShouldMatchEverySeamRootSpecialization()
    {
        Unsafe.SizeOf<NavigationAutomaticSeamPairKey>().Should().Be(48);
        Unsafe.SizeOf<NavigationCellAddress>().Should().Be(24);
        Unsafe.SizeOf<NavigationAutomaticSeamMapKey>().Should().Be(8);
        Unsafe.SizeOf<NavigationAutomaticSeamLinkKey>().Should().Be(16);
        Unsafe.SizeOf<NavigationSeamEditToken>().Should().Be(8);

        NavigationAutomaticSeamIndex.PairNodeBytes.Should().Be(104);
        NavigationAutomaticSeamIndex.AddressNodeBytes.Should().Be(80);
        NavigationAutomaticSeamIndex.MapNodeBytes.Should().Be(64);
        NavigationAutomaticSeamIndex.PairCursorShellBytes.Should().Be(144);
        NavigationAutomaticSeamIndex.AddressCursorShellBytes.Should().Be(96);
        NavigationAutomaticSeamIndex.MapCursorShellBytes.Should().Be(64);
        NavigationAutomaticSeamIndex.LinkCursorShellBytes.Should().Be(80);
    }

    [Fact]
    public void Editor_ShouldOwnEachMutableAvlNodeOnceAndPreserveTheSource()
    {
        var tree = new NavigationSeamEditTree<int, Box>(nodeBytes: 64);
        NavigationSeamEditTree<int, Box>.Editor first = tree.Edit(
            NavigationSeamEditToken.Create());
        first.Set(3, new Box(3));
        first.Set(2, new Box(2));
        first.Set(1, new Box(1));

        first.OwnedNodeCount.Should().Be(3);
        first.RetainedBytes.Should().Be(56L + (3L * 64L));
        first.PersistentPageCount.Should().Be(4);
        NavigationSeamEditTree<int, Box> published = first.Seal();
        published.RetainedBytes.Should().Be(32L + (3L * 64L));
        published.PersistentPageCount.Should().Be(4);
        tree.Count.Should().Be(0);

        NavigationSeamEditTree<int, Box>.Editor replacement = published.Edit(
            NavigationSeamEditToken.Create());
        var next = new Box(20);
        replacement.Set(2, next);
        replacement.OwnedNodeCount.Should().Be(1,
            "the root match is copied once and its unchanged children remain shared");
        NavigationSeamEditTree<int, Box> replaced = replacement.Seal();
        published.TryGetValue(2, out Box prior).Should().BeTrue();
        prior.Value.Should().Be(2);
        replaced.TryGetValue(2, out Box current).Should().BeTrue();
        current.Should().BeSameAs(next);
    }

    [Fact]
    public void AbsentRemove_ShouldNotCopyAnySearchPath()
    {
        var empty = new NavigationSeamEditTree<int, Box>(nodeBytes: 64);
        NavigationSeamEditTree<int, Box>.Editor seed = empty.Edit(
            NavigationSeamEditToken.Create());
        seed.Set(2, new Box(2));
        seed.Set(1, new Box(1));
        seed.Set(3, new Box(3));
        NavigationSeamEditTree<int, Box> source = seed.Seal();

        NavigationSeamEditTree<int, Box>.Editor editor = source.Edit(
            NavigationSeamEditToken.Create());
        editor.Remove(4).Should().BeFalse();
        editor.OwnedNodeCount.Should().Be(0);
        editor.IsChanged.Should().BeFalse();
        editor.Seal().Should().BeSameAs(source);
    }

    [Fact]
    public void Cursor_ShouldEnumerateAStableCanonicalRangeWithoutOrdinalWalks()
    {
        var empty = new NavigationSeamEditTree<int, Box>(nodeBytes: 64);
        NavigationSeamEditTree<int, Box>.Editor editor = empty.Edit(
            NavigationSeamEditToken.Create());
        for (int i = 7; i >= 0; i--)
            editor.Set(i, new Box(i));
        NavigationSeamEditTree<int, Box> tree = editor.Seal();
        NavigationSeamEditTree<int, Box>.Cursor cursor = tree.CreateCursor(16, shellBytes: 56);

        cursor.Begin(tree, minimum: 2, maximum: 6);
        int expected = 2;
        while (cursor.MoveNext())
            cursor.Current.Value.Should().Be(expected++);
        expected.Should().Be(6);
        cursor.BeginAll(new NavigationSeamEditTree<int, Box>(nodeBytes: 64));
        cursor.HasNext.Should().BeFalse();
        cursor.Current.Should().BeNull(
            "restarting a retained cursor releases its prior current value and stack");
        cursor.RetainedBytes.Should().Be(56L + 24L + (16L * 8L));
        cursor.PersistentPageCount.Should().Be(2);
    }

    [Fact]
    public void SealedEditor_ShouldRejectFurtherWrites()
    {
        var tree = new NavigationSeamEditTree<int, Box>(nodeBytes: 64);
        NavigationSeamEditTree<int, Box>.Editor editor = tree.Edit(
            NavigationSeamEditToken.Create());
        editor.Seal();

        Action set = () => editor.Set(1, new Box(1));
        Action remove = () => editor.Remove(1);
        set.Should().Throw<InvalidOperationException>();
        remove.Should().Throw<InvalidOperationException>();
    }

    private sealed class Box
    {
        internal Box(int value) => Value = value;

        internal int Value { get; }
    }
}
