using FixedMathSharp;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

// TODO: these should be in a seperate utility project

namespace Trailblazer.Support;

/// <summary>
/// Defines support for runtime properties marked with <see cref="TransientAttribute"/>.
/// <para>
/// In Trailblazer, "transient" means frame-local state that can be synchronized from another
/// instance or cleared back to defaults. The attribute does not control serialization on its own.
/// </para>
/// </summary>
public interface ITransient
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _transientPropertiesCache = new();

    /// <summary>
    /// Retrieves transient properties for a given type, caching results for performance.
    /// </summary>
    private static PropertyInfo[] GetTransientProperties(Type type)
    {
        return _transientPropertiesCache.GetOrAdd(type, t =>
            t.GetProperties().Where(p => p.IsDefined(typeof(TransientAttribute), false)).ToArray());
    }

    /// <summary>
    /// Synchronizes transient properties with another instance.
    /// </summary>
    /// <param name="other">The other instance to sync with.</param>
    public void SyncTransientState(ITransient other)
    {
        Debug.Assert(other != null, "Target cannot be null.");
        Type sourceType = GetType();
        Debug.Assert(sourceType == other.GetType(), "Type mismatch during SyncState.");

        foreach (var prop in GetTransientProperties(sourceType))
            prop.SetValue(this, prop.GetValue(other));
    }

    /// <summary>
    /// Clears all transient properties by resetting them to their default values.
    /// </summary>
    public void ClearTransientState()
    {
        Type sourceType = GetType();
        foreach (var prop in GetTransientProperties(sourceType))
        {
            object defaultValue;

            // Special cases for custom structs
            if (prop.PropertyType == typeof(Fixed4x4))
                defaultValue = Fixed4x4.Identity;
            else if (prop.PropertyType == typeof(FixedQuaternion))
                defaultValue = FixedQuaternion.Identity;
            else
                // Default for value types (int, bool, enums, Fixed64, etc.), null for reference types
                defaultValue = prop.PropertyType.IsValueType ? Activator.CreateInstance(prop.PropertyType) : null;

            prop.SetValue(this, defaultValue);
        }
    }
}
