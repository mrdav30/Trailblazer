using System;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationAutomaticSeamIndexOwnershipTests
{
    [Fact]
    public void SameEditPairReplacement_ShouldReleaseTheDisplacedOwnedPair()
    {
        var firstAddress = new NavigationCellAddress("A", default);
        var secondAddress = new NavigationCellAddress("B", default);
        var first = new NavigationAutomaticSeamPair(firstAddress, secondAddress, default);
        var replacement = new NavigationAutomaticSeamPair(
            firstAddress,
            secondAddress,
            default);
        var key = new NavigationAutomaticSeamPairKey(firstAddress, secondAddress);
        NavigationAutomaticSeamIndex.EditSession edit =
            NavigationAutomaticSeamIndex.Empty.Edit(NavigationSeamEditToken.Create());

        edit.SetPair(key, new NavigationAutomaticSeamPairRecord(first, isActive: true));
        edit.SetPair(key, new NavigationAutomaticSeamPairRecord(replacement, isActive: false));

        edit.SealedAdditionalRetainedBytes.Should().Be(432,
            "only the final pair record and geometry remain owned by the edit");
        edit.SealedAdditionalPersistentPages.Should().Be(4);
        NavigationAutomaticSeamIndex result = edit.Seal();
        result.PairCount.Should().Be(1);
        result.TryGetPair(firstAddress, secondAddress, out NavigationAutomaticSeamPair published)
            .Should().BeTrue();
        published.Should().BeSameAs(replacement);
        result.IsActive(new NavigationAutomaticSeamRef(replacement, reverse: false))
            .Should().BeFalse();
        result.IsActive(new NavigationAutomaticSeamRef(first, reverse: false))
            .Should().BeFalse("displaced geometry must not remain active under the same key");
        var missing = new NavigationAutomaticSeamPair(
            firstAddress,
            new NavigationCellAddress("C", default),
            default);
        result.IsActive(new NavigationAutomaticSeamRef(missing, reverse: false))
            .Should().BeFalse("unpublished seam geometry cannot be active");
        result.RetainedBytes.Should().Be(528);
        result.PersistentPageCount.Should().Be(7);
    }

    [Fact]
    public void SameEditRecordReplacement_ShouldRetainSharedPairGeometryOnce()
    {
        var firstAddress = new NavigationCellAddress("A", default);
        var secondAddress = new NavigationCellAddress("B", default);
        var pair = new NavigationAutomaticSeamPair(firstAddress, secondAddress, default);
        var key = new NavigationAutomaticSeamPairKey(firstAddress, secondAddress);
        NavigationAutomaticSeamIndex.EditSession edit =
            NavigationAutomaticSeamIndex.Empty.Edit(NavigationSeamEditToken.Create());

        edit.SetPair(key, new NavigationAutomaticSeamPairRecord(pair, isActive: true));
        edit.SetPair(key, new NavigationAutomaticSeamPairRecord(pair, isActive: false));

        edit.SealedAdditionalRetainedBytes.Should().Be(432,
            "replacing only record state must keep one owned copy of the shared geometry");
        edit.SealedAdditionalPersistentPages.Should().Be(4);
        NavigationAutomaticSeamIndex result = edit.Seal();
        result.TryGetPair(firstAddress, secondAddress, out NavigationAutomaticSeamPair published)
            .Should().BeTrue();
        published.Should().BeSameAs(pair);
        result.IsActive(new NavigationAutomaticSeamRef(pair, reverse: false)).Should().BeFalse();
        result.RetainedBytes.Should().Be(528);
        result.PersistentPageCount.Should().Be(7);
    }

    [Fact]
    public void SourcePairStateFlips_ShouldReuseSharedGeometryWithoutDoubleCharging()
    {
        var firstAddress = new NavigationCellAddress("A", default);
        var secondAddress = new NavigationCellAddress("B", default);
        var pair = new NavigationAutomaticSeamPair(firstAddress, secondAddress, default);
        var key = new NavigationAutomaticSeamPairKey(firstAddress, secondAddress);
        NavigationAutomaticSeamIndex.EditSession sourceEdit =
            NavigationAutomaticSeamIndex.Empty.Edit(NavigationSeamEditToken.Create());
        sourceEdit.SetPair(key, new NavigationAutomaticSeamPairRecord(pair, isActive: true));
        NavigationAutomaticSeamIndex source = sourceEdit.Seal();

        NavigationAutomaticSeamIndex.EditSession replacement =
            source.Edit(NavigationSeamEditToken.Create());
        replacement.SetPair(key, new NavigationAutomaticSeamPairRecord(pair, isActive: false));
        replacement.SetPair(key, new NavigationAutomaticSeamPairRecord(pair, isActive: true));
        NavigationAutomaticSeamIndex result = replacement.Seal();

        result.IsActive(new NavigationAutomaticSeamRef(pair, reverse: false)).Should().BeTrue();
        result.RetainedBytes.Should().Be(source.RetainedBytes,
            "record replacement must retain the source-owned pair geometry exactly once");
        result.PersistentPageCount.Should().Be(source.PersistentPageCount);
    }

    [Fact]
    public void SameEditDependencyRowReplacement_ShouldChargeOnlyTheFinalOwnedRow()
    {
        var source = new NavigationCellAddress("A", default);
        var firstPair = new NavigationAutomaticSeamPair(
            source,
            new NavigationCellAddress("B", default),
            default);
        var secondPair = new NavigationAutomaticSeamPair(
            source,
            new NavigationCellAddress("C", default),
            default);
        NavigationPagedSequence<NavigationAutomaticSeamPair> first = CreatePairs(firstPair);
        NavigationPagedSequence<NavigationAutomaticSeamPair> replacement = CreatePairs(
            firstPair,
            secondPair);
        NavigationAutomaticSeamIndex.EditSession edit =
            NavigationAutomaticSeamIndex.Empty.Edit(NavigationSeamEditToken.Create());

        edit.SetDependencyRow(source, first);
        edit.SetDependencyRow(source, replacement);

        edit.SealedAdditionalRetainedBytes.Should().Be(360,
            "the first same-session row must be released before the replacement is charged");
        edit.SealedAdditionalPersistentPages.Should().Be(5);
        NavigationAutomaticSeamIndex result = edit.Seal();
        NavigationPagedSequence<NavigationAutomaticSeamPair> row =
            result.GetDependencyRow(source);
        row.Count.Should().Be(2);
        row[0].Should().BeSameAs(firstPair);
        row[1].Should().BeSameAs(secondPair);
        result.RetainedBytes.Should().Be(456);
        result.PersistentPageCount.Should().Be(8);
    }

    [Fact]
    public void DependencyRowRevert_ShouldReleaseReplacementAndReuseSourcePayload()
    {
        var sourceAddress = new NavigationCellAddress("A", default);
        var firstPair = new NavigationAutomaticSeamPair(
            sourceAddress,
            new NavigationCellAddress("B", default),
            default);
        var secondPair = new NavigationAutomaticSeamPair(
            sourceAddress,
            new NavigationCellAddress("C", default),
            default);
        NavigationPagedSequence<NavigationAutomaticSeamPair> original = CreatePairs(firstPair);
        NavigationAutomaticSeamIndex.EditSession sourceEdit =
            NavigationAutomaticSeamIndex.Empty.Edit(NavigationSeamEditToken.Create());
        sourceEdit.SetDependencyRow(sourceAddress, original);
        NavigationAutomaticSeamIndex source = sourceEdit.Seal();
        NavigationPagedSequence<NavigationAutomaticSeamPair> replacement = CreatePairs(
            firstPair,
            secondPair);
        NavigationAutomaticSeamIndex.EditSession edit =
            source.Edit(NavigationSeamEditToken.Create());

        edit.SetDependencyRow(sourceAddress, replacement);
        edit.SetDependencyRow(sourceAddress, original);

        NavigationAutomaticSeamIndex result = edit.Seal();
        result.GetDependencyRow(sourceAddress).Should().BeSameAs(original);
        result.RetainedBytes.Should().Be(source.RetainedBytes,
            "reverting in one edit must not charge the source-owned row again");
        result.PersistentPageCount.Should().Be(source.PersistentPageCount);
    }

    [Fact]
    public void SealedEdit_ShouldRejectASecondSeal()
    {
        NavigationAutomaticSeamIndex.EditSession edit =
            NavigationAutomaticSeamIndex.Empty.Edit(NavigationSeamEditToken.Create());
        edit.SetStructuralLinks(
            "A",
            CreateLinks(new NavigationStructuralLink("B", 1, 0)));

        edit.Seal();

        Action secondSeal = () => edit.Seal();
        secondSeal.Should().Throw<InvalidOperationException>()
            .WithMessage("The seam index edit is already sealed.");
    }

    [Fact]
    public void SameEditStructuralLinkReplacement_ShouldChargeOnlyTheFinalOwnedRow()
    {
        NavigationPagedSequence<NavigationStructuralLink> first = CreateLinks(
            new NavigationStructuralLink("B", count: 1, uncertifiedCount: 0));
        NavigationPagedSequence<NavigationStructuralLink> replacement = CreateLinks(
            new NavigationStructuralLink("B", count: 2, uncertifiedCount: 1),
            new NavigationStructuralLink("C", count: 1, uncertifiedCount: 0));
        NavigationAutomaticSeamIndex.EditSession edit =
            NavigationAutomaticSeamIndex.Empty.Edit(NavigationSeamEditToken.Create());

        edit.SetStructuralLinks("A", first);
        edit.SetStructuralLinks("A", replacement);

        edit.SealedAdditionalRetainedBytes.Should().Be(408);
        edit.SealedAdditionalPersistentPages.Should().Be(5);
        NavigationAutomaticSeamIndex result = edit.Seal();
        NavigationPagedSequence<NavigationStructuralLink> links = result.GetStructuralLinks("A");
        links.Count.Should().Be(2);
        links[0].Should().Be(new NavigationStructuralLink("B", 2, 1));
        links[1].Should().Be(new NavigationStructuralLink("C", 1, 0));
        result.RetainedBytes.Should().Be(504);
        result.PersistentPageCount.Should().Be(8);
    }

    private static NavigationPagedSequence<NavigationStructuralLink> CreateLinks(
        params NavigationStructuralLink[] links)
    {
        var builder = new NavigationPagedSequence<NavigationStructuralLink>.Builder(
            elementBytes: 16);
        foreach (NavigationStructuralLink link in links)
            builder.Append(link);
        return builder.Seal();
    }

    private static NavigationPagedSequence<NavigationAutomaticSeamPair> CreatePairs(
        params NavigationAutomaticSeamPair[] pairs)
    {
        var builder = new NavigationPagedSequence<NavigationAutomaticSeamPair>.Builder(
            elementBytes: 8);
        foreach (NavigationAutomaticSeamPair pair in pairs)
            builder.Append(pair);
        return builder.Seal();
    }
}
