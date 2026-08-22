using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Query;

public sealed class NavigationContractArchitectureTests
{
    private static readonly Type[] ImmutableQueryContractTypes =
    {
        typeof(KinematicBodyShape),
        typeof(NavigationAgentProfile),
        typeof(NavigationAreaId),
        typeof(NavigationAreaPolicyKey),
        typeof(NavigationAreaRule),
        typeof(NavigationWorkBudget),
        typeof(MaintenanceWorkBudget),
        typeof(GuideSampleWorkBudget),
        typeof(NavigationEndpoint),
        typeof(TraversalIntent),
        typeof(FlowFieldQueryOptions),
        typeof(PathQuery)
    };

    private static readonly Type[] AuthoredNavigationContractTypes =
    {
        typeof(EndpointResolutionPolicy),
        typeof(FlowFieldQueryOptions),
        typeof(GuideSampleWorkBudget),
        typeof(KinematicBodyShape),
        typeof(NavigationAgentProfile),
        typeof(NavigationAreaId),
        typeof(NavigationAreaPolicy),
        typeof(NavigationAreaPolicyCommitOperation),
        typeof(NavigationAreaPolicyKey),
        typeof(NavigationAreaRule),
        typeof(NavigationCell),
        typeof(NavigationCellAddress),
        typeof(NavigationCellEntry),
        typeof(NavigationCellFlags),
        typeof(NavigationCellOverlayOperation),
        typeof(NavigationCellOverlayOperationKind),
        typeof(NavigationConnection),
        typeof(NavigationConnectionOverlayOperation),
        typeof(NavigationConnectionOverlayOperationKind),
        typeof(NavigationEndpoint),
        typeof(NavigationMap),
        typeof(NavigationMapBuilder),
        typeof(NavigationMapCheckpointStamp),
        typeof(NavigationMapCommitOperation),
        typeof(NavigationMapOverlayDelta),
        typeof(NavigationMapTokenImporter),
        typeof(NavigationMapRemoveOperation),
        typeof(NavigationTokenLegend),
        typeof(NavigationTokenLegendEntry),
        typeof(NavigationOperationLimits),
        typeof(NavigationOperationReceipt),
        typeof(NavigationOperationRejection),
        typeof(NavigationOperationStatus),
        typeof(NavigationOverlayCommitOperation),
        typeof(NavigationOverlayTransaction),
        typeof(NavigationWorkBudget),
        typeof(OverlayReplacementPolicy),
        typeof(PathAlgorithm),
        typeof(PathQuery),
        typeof(PreparedNavigationMap),
        typeof(PreparedNavigationOverlay),
        typeof(TraversalCapability),
        typeof(TraversalIntent),
        typeof(TraversalTransitionDefinition),
        typeof(TraversalTransitionLocomotionHints),
        typeof(TraversalTransitionRule),
        typeof(TraversalTransitionRuleScope),
        typeof(TraversalTransitionOverlayOperation),
        typeof(TraversalTransitionOverlayOperationKind)
    };

    private static readonly Type[] RuntimeNavigationContractTypes =
    {
        typeof(MaintenanceWorkBudget),
        typeof(NavigationCellLookupKind),
        typeof(NavigationCellSemanticSource),
        typeof(NavigationGraphCellDiagnostic),
        typeof(NavigationGraphMapDiagnostic),
        typeof(NavigationGraphDiagnosticsSnapshot),
        typeof(TrailblazerWorldContextSettings)
    };

    [Fact]
    public void QueryContracts_ShouldBeImmutableValueTypes()
    {
        foreach (Type contractType in ImmutableQueryContractTypes)
        {
            contractType.IsValueType.Should().BeTrue($"{contractType.Name} is an immutable value contract");
            contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Should().OnlyContain(property => property.SetMethod == null);
            contractType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Should().OnlyContain(field => field.IsInitOnly);
        }
    }

    [Fact]
    public void AuthoredNavigationContracts_ShouldNotExposeRuntimeGridIdentity()
    {
        string[] bannedExactTypeNames =
        {
            "GridForge.Spatial.WorldVoxelIndex",
            "GridForge.Grids.Voxel"
        };
        string[] bannedNameFragments =
        {
            "GridSlot",
            "GridGeneration"
        };
        string[] exposedTypeNames = AuthoredNavigationContractTypes
            .SelectMany(GetPublicSignatureTypes)
            .SelectMany(ExpandTypeGraph)
            .Select(type => type.FullName ?? type.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        exposedTypeNames.Should().NotContain(
            name => bannedExactTypeNames.Contains(name, StringComparer.Ordinal)
                || bannedNameFragments.Any(fragment => name.Contains(fragment, StringComparison.Ordinal)),
            "authored navigation contracts must remain independent from runtime voxel identity and grid slots/generations");
    }

    [Fact]
    public void AuthoredCellIdentity_ShouldNotIncludeStorageKind()
    {
        Type[] authoredCellTypes = { typeof(NavigationCell), typeof(NavigationCellEntry) };
        string[] signatureNames = authoredCellTypes
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(member => member.Name)
            .Concat(authoredCellTypes.SelectMany(GetPublicSignatureTypes).Select(type => type.FullName ?? type.Name))
            .ToArray();

        signatureNames.Should().NotContain(
            name => name.Contains("StorageKind", StringComparison.Ordinal)
                || name.Contains("GridStorage", StringComparison.Ordinal),
            "dense and sparse materialization must share one authored cell identity");
    }

    [Fact]
    public void RuntimeNavigationContracts_ShouldNotExposeVoxelObjectsOrRuntimePartitionOwnership()
    {
        string[] exposedTypeNames = RuntimeNavigationContractTypes
            .SelectMany(GetPublicSignatureTypes)
            .SelectMany(ExpandTypeGraph)
            .Select(type => type.FullName ?? type.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        exposedTypeNames.Should().NotContain(
            name => string.Equals(name, "GridForge.Grids.Voxel", StringComparison.Ordinal)
                || string.Equals(name, "GridForge.Spatial.WorldVoxelIndex", StringComparison.Ordinal)
                || name.Contains("Partition", StringComparison.Ordinal),
            "the context graph owns exact copied identity and never exposes GridForge voxel or runtime partition ownership");
    }

    [Fact]
    public void RuntimeNavigationContracts_ShouldNotExposeRecyclableRuntimeGridSlots()
    {
        string[] publicMemberNames = RuntimeNavigationContractTypes
            .SelectMany(type => type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(member => member.Name)
            .ToArray();

        publicMemberNames.Should().NotContain(
            name => string.Equals(name, "GridIndex", StringComparison.Ordinal)
                || name.Contains("GridSlot", StringComparison.Ordinal)
                || name.Contains("RuntimeSlot", StringComparison.Ordinal),
            "public map identity uses world and grid generation tokens rather than recyclable runtime slots");
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Type contractType)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        yield return contractType;

        foreach (FieldInfo field in contractType.GetFields(Flags))
            yield return field.FieldType;
        foreach (PropertyInfo property in contractType.GetProperties(Flags))
            yield return property.PropertyType;
        foreach (ConstructorInfo constructor in contractType.GetConstructors(Flags))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
                yield return parameter.ParameterType;
        }
        foreach (MethodInfo method in contractType.GetMethods(Flags))
        {
            yield return method.ReturnType;
            foreach (ParameterInfo parameter in method.GetParameters())
                yield return parameter.ParameterType;
        }
    }

    private static IEnumerable<Type> ExpandTypeGraph(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is Type elementType)
        {
            foreach (Type expanded in ExpandTypeGraph(elementType))
                yield return expanded;
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            foreach (Type expanded in ExpandTypeGraph(argument))
                yield return expanded;
        }
    }
}
