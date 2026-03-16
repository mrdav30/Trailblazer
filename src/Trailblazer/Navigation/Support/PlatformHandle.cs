using FixedMathSharp;
using System;

#if NET8_0_OR_GREATER
using System.Text.Json.Serialization;
#endif
#if !NET8_0_OR_GREATER
using System.Text.Json.Serialization.Shim;
#endif

namespace Trailblazer.Navigation;

public struct PlatformHandle : IEquatable<PlatformHandle>
{
    public readonly int Id;

    public Fixed4x4 Transform;

    public readonly bool Active => Id != 0;

    [JsonConstructor]
    public PlatformHandle(int id, Fixed4x4 transform)
    {
        Id = id;
        Transform = transform;
    }

    public static bool operator ==(PlatformHandle left, PlatformHandle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PlatformHandle left, PlatformHandle right)
    {
        return !(left == right);
    }

    public readonly bool Equals(PlatformHandle other) => Id == other.Id;
    public override readonly bool Equals(object obj) => obj is PlatformHandle h && Equals(h);
    public override readonly int GetHashCode() => Id;
}
