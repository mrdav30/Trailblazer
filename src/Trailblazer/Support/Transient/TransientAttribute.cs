using System;

namespace Trailblazer.Support;

/// <summary>
/// Marks a property as transient, indicating that it holds frame-local state
/// that can be synchronized from another instance or cleared back to defaults.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class TransientAttribute : Attribute { }
