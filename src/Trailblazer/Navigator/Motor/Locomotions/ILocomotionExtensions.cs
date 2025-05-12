using FixedMathSharp;
using System.Collections.Concurrent;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Trailblazer.Navigator.Motor;

[AttributeUsage(AttributeTargets.Property)]
public class TransientAttribute : Attribute { }

// TODO: these should be in a seperate utility project
public static class ILocomotionExtensions
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
    /// Synchronizes all transient properties between two locomotion instances.
    /// </summary>
    public static void SyncTransientState(this ITransientLocomotion source, ITransientLocomotion target)
    {
        Debug.Assert(source != null, "Source locomotion cannot be null.");
        Debug.Assert(target != null, "Target locomotion cannot be null.");
        Debug.Assert(source.GetType() == target.GetType(), "Locomotion type mismatch during SyncState.");

        foreach (var prop in GetTransientProperties(source.GetType()))
            prop.SetValue(source, prop.GetValue(target));
    }

    /// <summary>
    /// Clears all transient properties by resetting them to their default values.
    /// </summary>
    public static void ClearTransientState(this ITransientLocomotion instance)
    {
        foreach (var prop in GetTransientProperties(instance.GetType()))
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

            prop.SetValue(instance, defaultValue);
        }
    }
}
