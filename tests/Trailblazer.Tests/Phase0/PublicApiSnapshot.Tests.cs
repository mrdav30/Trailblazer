using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Phase0;

/// <summary>
/// Guards the pre-refactor public surface so each intentional clean break is visible in review.
/// </summary>
public sealed class PublicApiSnapshotTests
{
    [Theory]
    [InlineData("Trailblazer.Pathing.PathRequest")]
    [InlineData("Trailblazer.Pathing.SolidVoxelFinder")]
    [InlineData("Trailblazer.Pathing.AlternativeVoxelFinder")]
    public void RetiredLegacyPathRequestFamily_ShouldBeDeleted(string typeName)
    {
        typeof(TrailblazerWorldContext).Assembly.GetType(typeName).Should().BeNull();
    }

    [Fact]
    public void RetainedVolumeAndHybridCarriers_ShouldExposeOnlyConcreteLiveState()
    {
        typeof(VolumeGuide).GetProperty("TrailMap").Should().BeNull();
        typeof(VolumeGuide).GetProperty("VolumeResult").Should().NotBeNull();
        typeof(VolumeSurveyor).GetProperty("Shared").Should().BeNull();
        typeof(HybridRouteStep).GetProperty("AdditionalCost").Should().BeNull();
    }

    [Fact]
    public void ExportedApi_ShouldMatchThePhase0Snapshot()
    {
        string snapshotPath = Path.Combine(AppContext.BaseDirectory, "Phase0", "PublicApiSnapshot.txt");
        string[] expected = File.ReadAllLines(snapshotPath)
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
        string[] actual = CaptureSnapshot(typeof(TrailblazerWorldContext).Assembly);
        actual.Should().Equal(expected,
            "public API changes during the clean-break refactor must update the checked-in Phase 0 snapshot intentionally");
    }

    private static string[] CaptureSnapshot(Assembly assembly)
    {
        return assembly.GetExportedTypes()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(CaptureType)
            .ToArray();
    }

    private static string CaptureType(Type type)
    {
        string[] signatures = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType != MemberTypes.NestedType)
            .Select(FormatMember)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();
        string typeContract = string.Join("\n", new[] { FormatTypeDeclaration(type) }.Concat(signatures));
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(typeContract)));
        return $"{FormatType(type)}|{signatures.Length.ToString(CultureInfo.InvariantCulture)}|{fingerprint}";
    }

    private static string FormatTypeDeclaration(Type type)
    {
        var declaration = new StringBuilder();
        declaration.Append(type.Attributes);
        declaration.Append('|');
        declaration.Append(FormatType(type.BaseType));
        declaration.Append('|');
        declaration.AppendJoin(',', type.GetInterfaces().Select(FormatType).OrderBy(name => name, StringComparer.Ordinal));
        declaration.Append('|');
        AppendGenericArguments(declaration, type.IsGenericTypeDefinition ? type.GetGenericArguments() : Type.EmptyTypes);
        return declaration.ToString();
    }

    private static string FormatMember(MemberInfo member)
    {
        return member switch
        {
            ConstructorInfo constructor => FormatMethodBase("ctor", constructor, returnType: null),
            MethodInfo method => FormatMethodBase("method", method, method.ReturnType),
            PropertyInfo property => FormatProperty(property),
            FieldInfo field => FormatField(field),
            EventInfo eventInfo => FormatEvent(eventInfo),
            _ => $"{member.MemberType}|{member.Name}"
        };
    }

    private static string FormatMethodBase(string kind, MethodBase method, Type? returnType)
    {
        var signature = new StringBuilder();
        signature.Append(kind);
        signature.Append('|');
        signature.Append(method.Attributes);
        signature.Append('|');
        signature.Append(method.CallingConvention);
        signature.Append('|');
        signature.Append(FormatType(returnType));
        signature.Append('|');
        signature.Append(method.Name);
        signature.Append('|');
        if (method is MethodInfo methodInfo)
            AppendGenericArguments(signature, methodInfo.IsGenericMethodDefinition ? methodInfo.GetGenericArguments() : Type.EmptyTypes);
        signature.Append('|');
        AppendParameters(signature, method.GetParameters());
        return signature.ToString();
    }

    private static string FormatProperty(PropertyInfo property)
    {
        var signature = new StringBuilder("property|");
        signature.Append(property.Name);
        signature.Append('|');
        signature.Append(FormatType(property.PropertyType));
        signature.Append("|get=");
        signature.Append(property.GetMethod?.Attributes.ToString() ?? "none");
        signature.Append("|set=");
        signature.Append(property.SetMethod?.Attributes.ToString() ?? "none");
        signature.Append('|');
        AppendParameters(signature, property.GetIndexParameters());
        return signature.ToString();
    }

    private static string FormatField(FieldInfo field)
    {
        var signature = new StringBuilder("field|");
        signature.Append(field.Attributes);
        signature.Append('|');
        signature.Append(FormatType(field.FieldType));
        signature.Append('|');
        signature.Append(field.Name);
        if (field.IsLiteral)
        {
            signature.Append('|');
            signature.Append(FormatValue(field.GetRawConstantValue()));
        }

        return signature.ToString();
    }

    private static string FormatEvent(EventInfo eventInfo)
    {
        return $"event|{eventInfo.Name}|{FormatType(eventInfo.EventHandlerType)}|" +
            $"add={eventInfo.AddMethod?.Attributes.ToString() ?? "none"}|" +
            $"remove={eventInfo.RemoveMethod?.Attributes.ToString() ?? "none"}";
    }

    private static void AppendParameters(StringBuilder signature, ParameterInfo[] parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
                signature.Append(',');

            ParameterInfo parameter = parameters[i];
            signature.Append(parameter.Attributes);
            signature.Append(':');
            signature.Append(FormatType(parameter.ParameterType));
            signature.Append(':');
            signature.Append(parameter.Name);
            if (parameter.HasDefaultValue)
            {
                signature.Append('=');
                signature.Append(FormatValue(parameter.DefaultValue));
            }
        }
    }

    private static void AppendGenericArguments(StringBuilder signature, Type[] arguments)
    {
        for (int i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
                signature.Append(',');

            Type argument = arguments[i];
            signature.Append(argument.Name);
            signature.Append(':');
            signature.Append(argument.GenericParameterAttributes);
            signature.Append(':');
            signature.AppendJoin('&', argument.GetGenericParameterConstraints().Select(FormatType).OrderBy(name => name, StringComparer.Ordinal));
        }
    }

    private static string FormatType(Type? type)
    {
        if (type == null)
            return "void";
        if (type.IsByRef)
            return $"{FormatType(type.GetElementType())}&";
        if (type.IsPointer)
            return $"{FormatType(type.GetElementType())}*";
        if (type.IsArray)
            return $"{FormatType(type.GetElementType())}[{new string(',', type.GetArrayRank() - 1)}]";
        if (type.IsGenericParameter)
            return $"`{type.GenericParameterPosition}:{type.Name}";
        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        string genericName = type.GetGenericTypeDefinition().FullName ?? type.Name;
        int tick = genericName.IndexOf('`');
        if (tick >= 0)
            genericName = genericName.Substring(0, tick);
        return $"{genericName}<{string.Join(",", type.GetGenericArguments().Select(FormatType))}>";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            char character => $"'{character}'",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }
}
