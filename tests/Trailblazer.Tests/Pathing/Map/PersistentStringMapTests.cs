using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Map;

public sealed class PersistentStringMapTests
{
    [Fact]
    public void RemoveTwoChildRoot_ShouldPreserveSourceAndCanonicalSurvivors()
    {
        PersistentStringMap<int> source = PersistentStringMap<int>.Empty;
        foreach (string key in new[] { "a", "b", "c", "d", "e", "f", "g" })
            source = source.Set(key, key[0]);

        PersistentStringMap<int> removed = source.Remove("d", out bool didRemove);
        PersistentStringMap<int> counted = source.Remove(
            "d",
            out bool didCountedRemove,
            out int copiedNodes);

        didRemove.Should().BeTrue();
        didCountedRemove.Should().BeTrue();
        Keys(source).Should().Equal("a", "b", "c", "d", "e", "f", "g");
        Keys(removed).Should().Equal("a", "b", "c", "e", "f", "g");
        Keys(counted).Should().Equal("a", "b", "c", "e", "f", "g");
        copiedNodes.Should().BeGreaterThan(0);
        copiedNodes.Should().BeLessThan(source.Count);
    }

    [Fact]
    public void RemoveLeftLeafAndOnlyLeftChild_ShouldPreserveCanonicalSnapshots()
    {
        PersistentStringMap<int> source = PersistentStringMap<int>.Empty
            .Set("b", 2)
            .Set("a", 1);

        PersistentStringMap<int> leafRemoved = source.Remove("a", out bool removedLeaf);
        PersistentStringMap<int> rootRemoved = source.Remove("b", out bool removedRoot);

        removedLeaf.Should().BeTrue();
        removedRoot.Should().BeTrue();
        Keys(leafRemoved).Should().Equal("b");
        Keys(rootRemoved).Should().Equal("a");
        Keys(source).Should().Equal("a", "b");
    }

    [Fact]
    public void MissingDeepRemoval_ShouldReuseEveryAncestorOnBothSearchSides()
    {
        PersistentStringMap<int> source = PersistentStringMap<int>.Empty;
        foreach (string key in new[] { "a", "b", "c", "d", "e", "f", "g" })
            source = source.Set(key, key[0]);

        source.Remove(string.Empty, out bool removedLeft)
            .Should().BeSameAs(source);
        source.Remove("zz", out bool removedRight)
            .Should().BeSameAs(source);
        source.Remove(string.Empty, out bool countedLeft, out int leftCopies)
            .Should().BeSameAs(source);
        source.Remove("zz", out bool countedRight, out int rightCopies)
            .Should().BeSameAs(source);

        removedLeft.Should().BeFalse();
        removedRight.Should().BeFalse();
        countedLeft.Should().BeFalse();
        countedRight.Should().BeFalse();
        leftCopies.Should().Be(0);
        rightCopies.Should().Be(0);
        Keys(source).Should().Equal("a", "b", "c", "d", "e", "f", "g");
    }

    private static string[] Keys(PersistentStringMap<int> map)
    {
        var keys = new string[map.Count];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = map.GetKeyAt(i);
        return keys;
    }
}
