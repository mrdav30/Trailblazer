//=======================================================================
// ITransient.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
    /// <summary>
    /// Synchronizes transient properties with another instance.
    /// </summary>
    /// <param name="other">The other instance to sync with.</param>
    public void SyncTransientState(ITransient other)
    {
        TransientStateUtility.Sync(this, other);
    }

    /// <summary>
    /// Clears all transient properties by resetting them to their default values.
    /// </summary>
    public void ClearTransientState()
    {
        TransientStateUtility.Clear(this);
    }
}
