//=======================================================================
// TransientAttribute.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Support;

/// <summary>
/// Marks a property as transient, indicating that it holds frame-local state
/// that can be synchronized from another instance or cleared back to defaults.
/// </summary>
/// <remarks>
/// When a default value source and member are provided via the two-argument constructor,
/// <see cref="ITransient.ClearTransientState"/> resets the property to that static member's value
/// instead of <c>default(T)</c>. Useful for types whose zero-value is not a valid reset state
/// (e.g. identity matrices or quaternions).
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public class TransientAttribute : Attribute
{
    /// <summary>
    /// Gets the type that declares the static member to use as the clear default, or <c>null</c>
    /// to use <c>default(T)</c>.
    /// </summary>
    public Type? DefaultValueSource { get; }

    /// <summary>
    /// Gets the name of the static field or property on <see cref="DefaultValueSource"/> to use
    /// as the clear default, or <c>null</c> to use <c>default(T)</c>.
    /// </summary>
    public string? DefaultValueMember { get; }

    /// <summary>
    /// Marks the property as transient and clears it to <c>default(T)</c> on reset.
    /// </summary>
    public TransientAttribute() { }

    /// <summary>
    /// Marks the property as transient and clears it to a specific static member value on reset.
    /// </summary>
    /// <param name="defaultValueSource">The type declaring the static default member.</param>
    /// <param name="defaultValueMember">The name of the static field or property to read.</param>
    public TransientAttribute(Type defaultValueSource, string defaultValueMember)
    {
        DefaultValueSource = defaultValueSource;
        DefaultValueMember = defaultValueMember;
    }
}
