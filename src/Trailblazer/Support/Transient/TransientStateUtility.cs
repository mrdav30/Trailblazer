using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Trailblazer.Support;

/// <summary>
/// Shared implementation for transient-state synchronization and reset behavior.
/// </summary>
/// <remarks>
/// Delegates are compiled once per type on first use via expression trees, so per-call overhead
/// is direct property assignment with no reflection at runtime.
/// </remarks>
internal static class TransientStateUtility
{
    private static readonly ConcurrentDictionary<Type, Action<ITransient, ITransient>> _syncDelegates = new();
    private static readonly ConcurrentDictionary<Type, Action<ITransient>> _clearDelegates = new();

    internal static void Sync(ITransient instance, ITransient other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        Type sourceType = instance.GetType();
        Type targetType = other.GetType();
        if (sourceType != targetType)
        {
            throw new ArgumentException(
                $"Type mismatch during SyncTransientState. Expected {sourceType.FullName}, but received {targetType.FullName}.",
                nameof(other));
        }

        _syncDelegates.GetOrAdd(sourceType, BuildSyncDelegate)(instance, other);
    }

    internal static void Clear(ITransient instance)
    {
        _clearDelegates.GetOrAdd(instance.GetType(), BuildClearDelegate)(instance);
    }

    private static Action<ITransient, ITransient> BuildSyncDelegate(Type type)
    {
        PropertyInfo[] properties = GetTransientProperties(type);
        if (properties.Length == 0)
            return static (_, _) => { };

        ParameterExpression instanceParam = Expression.Parameter(typeof(ITransient), "instance");
        ParameterExpression otherParam = Expression.Parameter(typeof(ITransient), "other");
        ParameterExpression typedInstance = Expression.Variable(type, "typedInstance");
        ParameterExpression typedOther = Expression.Variable(type, "typedOther");

        Expression[] body = new Expression[2 + properties.Length];
        body[0] = Expression.Assign(typedInstance, Expression.Convert(instanceParam, type));
        body[1] = Expression.Assign(typedOther, Expression.Convert(otherParam, type));
        for (int i = 0; i < properties.Length; i++)
            body[i + 2] = Expression.Assign(
                Expression.Property(typedInstance, properties[i]),
                Expression.Property(typedOther, properties[i]));

        BlockExpression block = Expression.Block(new[] { typedInstance, typedOther }, body);
        return Expression.Lambda<Action<ITransient, ITransient>>(block, instanceParam, otherParam).Compile();
    }

    private static Action<ITransient> BuildClearDelegate(Type type)
    {
        PropertyInfo[] properties = GetTransientProperties(type);
        if (properties.Length == 0)
            return static _ => { };

        ParameterExpression instanceParam = Expression.Parameter(typeof(ITransient), "instance");
        ParameterExpression typedVar = Expression.Variable(type, "typed");

        Expression[] body = new Expression[1 + properties.Length];
        body[0] = Expression.Assign(typedVar, Expression.Convert(instanceParam, type));
        for (int i = 0; i < properties.Length; i++)
            body[i + 1] = Expression.Assign(
                Expression.Property(typedVar, properties[i]),
                GetDefaultExpression(properties[i]));

        BlockExpression block = Expression.Block(new[] { typedVar }, body);
        return Expression.Lambda<Action<ITransient>>(block, instanceParam).Compile();
    }

    private static Expression GetDefaultExpression(PropertyInfo property)
    {
        TransientAttribute? attr = property.GetCustomAttribute<TransientAttribute>();
        if (attr?.DefaultValueSource != null && attr.DefaultValueMember != null)
            return GetStaticMemberExpression(attr.DefaultValueSource, attr.DefaultValueMember);

        return Expression.Default(property.PropertyType);
    }

    private static Expression GetStaticMemberExpression(Type type, string memberName)
    {
        FieldInfo? field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Static);
        if (field != null)
            return Expression.Field(null, field);

        PropertyInfo? prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static);
        if (prop == null)
            throw new InvalidOperationException(
                $"Transient default member '{type.FullName}.{memberName}' was not found.");

        return Expression.Property(null, prop);
    }

    private static PropertyInfo[] GetTransientProperties(Type type)
    {
        return type.GetProperties()
            .Where(static p => p.IsDefined(typeof(TransientAttribute), false))
            .ToArray();
    }
}
