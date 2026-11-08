//=======================================================================
// NavigationOperationRejection.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies why a navigation operation was rejected.</summary>
public enum NavigationOperationRejection : byte
{
    /// <summary>The receipt is not rejected.</summary>
    None = 0,

    /// <summary>The operation descriptor failed structural validation.</summary>
    InvalidOperation = 1,

    /// <summary>The supplied operation sequence was already admitted.</summary>
    DuplicateSequence = 2,

    /// <summary>The supplied operation sequence was below the admitted high-water mark.</summary>
    RegressingSequence = 3,

    /// <summary>The effective frame regressed below the admitted frame high-water mark.</summary>
    RegressingEffectiveFrame = 4,

    /// <summary>The effective-frame boundary had already begun when the operation was submitted.</summary>
    LateEffectiveFrame = 5,

    /// <summary>A finite count or byte capacity would be exceeded.</summary>
    CapacityExceeded = 6,

    /// <summary>The operation referenced a map that was not present in the candidate.</summary>
    MissingMap = 7,

    /// <summary>The operation was based on a stale checkpoint stamp.</summary>
    Stale = 8,

    /// <summary>The complete candidate failed validation.</summary>
    ValidationFailed = 9
}
