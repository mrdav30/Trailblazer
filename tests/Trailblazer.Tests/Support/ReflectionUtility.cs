using System;
using System.Reflection;

namespace Trailblazer.Tests;

public static class ReflectionUtility
{
    internal static T GetPrivateField<T>(object instance, string fieldName)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was not found on {instance.GetType().Name}.");
        return (T)field.GetValue(instance)!;
    }

    internal static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was not found on {instance.GetType().Name}.");
        field.SetValue(instance, value);
    }

    internal static TReturn InvokePrivate<TReturn>(object instance, string methodName, params object[] arguments)
    {
        MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {instance.GetType().Name}.");
        return (TReturn)method.Invoke(instance, arguments)!;
    }

    internal static T GetPrivateFieldFromBase<T>(object instance, string fieldName)
    {
        Type baseType = instance.GetType().BaseType
            ?? throw new InvalidOperationException($"{instance.GetType().Name} does not have a base type.");
        FieldInfo field = baseType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was not found on {baseType.Name}.");
        return (T)field.GetValue(instance)!;
    }

    internal static TReturn InvokePrivateStatic<TReturn>(Type type, string methodName, params object[] arguments)
    {
        MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {type.Name}.");
        return (TReturn)method.Invoke(null, arguments)!;
    }

    internal static TReturn InvokePrivateStatic<TReturn>(
        Type type,
        string methodName,
        Type[] parameterTypes,
        params object[] arguments)
    {
        MethodInfo method = type.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null)
            ?? throw new InvalidOperationException($"Method '{methodName}' with the requested signature was not found on {type.Name}.");
        return (TReturn)method.Invoke(null, arguments)!;
    }

    internal static T GetPrivateStaticField<T>(Type type, string fieldName)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Static field '{fieldName}' was not found on {type.Name}.");
        return (T)field.GetValue(null)!;
    }

    internal static void SetPrivateStaticField<T>(Type type, string fieldName, T value)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Static field '{fieldName}' was not found on {type.Name}.");
        field.SetValue(null, value);
    }
}
