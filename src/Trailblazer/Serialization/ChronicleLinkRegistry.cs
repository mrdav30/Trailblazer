using System;
using System.Collections.Generic;

namespace Trailblazer.Serialization;

/// <summary>
/// Stores stable-link resolution strategies for external or runtime-owned objects.
/// </summary>
public sealed class ChronicleLinkRegistry
{
    private readonly Dictionary<ChronicleLinkKey, object> _resolvers = new();
    private readonly Dictionary<ChronicleLinkKey, object> _registeredInstances = new();

    /// <summary>
    /// Registers a custom link resolver for the given type and optional slot.
    /// </summary>
    public void RegisterResolver<T>(IRecordLinkResolver<T> resolver, string slot = null)
    {
        if (resolver == null)
            throw new ArgumentNullException(nameof(resolver));

        _resolvers[new ChronicleLinkKey(typeof(T), slot)] = resolver;
    }

    /// <summary>
    /// Registers a concrete instance so the chronicler can save and load it through a stable identifier.
    /// </summary>
    public void RegisterInstance<T>(string id, T value, string slot = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A registered link id must not be null or empty.", nameof(id));

        RegisteredLinkTable<T> table = GetOrCreateInstanceTable<T>(slot);
        table.Register(id, value);
    }

    /// <summary>
    /// Removes a previously registered concrete instance.
    /// </summary>
    public bool UnregisterInstance<T>(string id, string slot = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        ChronicleLinkKey key = new(typeof(T), slot);
        if (!_registeredInstances.TryGetValue(key, out object tableObject))
            return false;

        return ((RegisteredLinkTable<T>)tableObject).Unregister(id);
    }

    /// <summary>
    /// Attempts to resolve a stable identifier into an instance of the requested type.
    /// </summary>
    public bool TryResolve<T>(string id, out T value, string slot = null)
    {
        ChronicleLinkKey key = new(typeof(T), slot);
        if (_resolvers.TryGetValue(key, out object resolverObject)
            && ((IRecordLinkResolver<T>)resolverObject).TryResolveReference(id, out value))
        {
            return true;
        }

        if (_registeredInstances.TryGetValue(key, out object tableObject)
            && ((RegisteredLinkTable<T>)tableObject).TryResolve(id, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Attempts to read a stable identifier from a concrete instance.
    /// </summary>
    public bool TryGetReferenceId<T>(T value, out string id, string slot = null)
    {
        ChronicleLinkKey key = new(typeof(T), slot);
        if (_resolvers.TryGetValue(key, out object resolverObject)
            && ((IRecordLinkResolver<T>)resolverObject).TryGetReferenceId(value, out id))
        {
            return true;
        }

        if (_registeredInstances.TryGetValue(key, out object tableObject)
            && ((RegisteredLinkTable<T>)tableObject).TryGetReferenceId(value, out id))
        {
            return true;
        }

        id = null;
        return false;
    }

    private RegisteredLinkTable<T> GetOrCreateInstanceTable<T>(string slot)
    {
        ChronicleLinkKey key = new(typeof(T), slot);
        if (_registeredInstances.TryGetValue(key, out object tableObject))
            return (RegisteredLinkTable<T>)tableObject;

        var table = new RegisteredLinkTable<T>();
        _registeredInstances[key] = table;
        return table;
    }

    private readonly struct ChronicleLinkKey : IEquatable<ChronicleLinkKey>
    {
        private readonly Type _type;
        private readonly string _slot;

        public ChronicleLinkKey(Type type, string slot)
        {
            _type = type ?? throw new ArgumentNullException(nameof(type));
            _slot = slot ?? string.Empty;
        }

        public bool Equals(ChronicleLinkKey other)
        {
            return _type == other._type
                && string.Equals(_slot, other._slot, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is ChronicleLinkKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (_type.GetHashCode() * 397) ^ _slot.GetHashCode();
            }
        }
    }

    private sealed class RegisteredLinkTable<T>
    {
        private readonly Dictionary<string, T> _byId = new(StringComparer.Ordinal);

        public void Register(string id, T value)
        {
            _byId[id] = value;
        }

        public bool Unregister(string id)
        {
            return _byId.Remove(id);
        }

        public bool TryResolve(string id, out T value)
        {
            return _byId.TryGetValue(id, out value);
        }

        public bool TryGetReferenceId(T value, out string id)
        {
            foreach (KeyValuePair<string, T> pair in _byId)
            {
                if (!ValuesMatch(pair.Value, value))
                    continue;

                id = pair.Key;
                return true;
            }

            id = null;
            return false;
        }

        private static bool ValuesMatch(T left, T right)
        {
            if (typeof(T).IsValueType)
                return EqualityComparer<T>.Default.Equals(left, right);

            return ReferenceEquals(left, right);
        }
    }
}
