using FixedMathSharp;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Trailblazer.Support;

/// <summary>
/// Shared implementation for transient-state synchronization and reset behavior.
/// </summary>
internal static class TransientStateUtility
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _transientPropertiesCache = new();

    internal static void Sync(ITransient instance, ITransient other)
    {
        Debug.Assert(other != null, "Target cannot be null.");

        Type sourceType = instance.GetType();
        Debug.Assert(sourceType == other.GetType(), "Type mismatch during SyncState.");

        foreach (PropertyInfo property in GetTransientProperties(sourceType))
            property.SetValue(instance, property.GetValue(other));
    }

    internal static void Clear(ITransient instance)
    {
        foreach (PropertyInfo property in GetTransientProperties(instance.GetType()))
            property.SetValue(instance, GetDefaultValue(property.PropertyType));
    }

    private static PropertyInfo[] GetTransientProperties(Type type)
    {
        return _transientPropertiesCache.GetOrAdd(type, static t =>
            t.GetProperties().Where(static p => p.IsDefined(typeof(TransientAttribute), false)).ToArray());
    }

    private static object GetDefaultValue(Type propertyType)
    {
        if (propertyType == typeof(Fixed4x4))
            return Fixed4x4.Identity;

        if (propertyType == typeof(FixedQuaternion))
            return FixedQuaternion.Identity;

        return propertyType.IsValueType
            ? Activator.CreateInstance(propertyType)
            : null;
    }
}
