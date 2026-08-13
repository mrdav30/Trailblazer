//=======================================================================
// GuidePool.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Small synchronized guide-wrapper pool used by hot cache-hit paths.
/// </summary>
internal sealed class GuidePool<T> where T : class, IGuide
{
    private const int DefaultInitialCapacity = 128;
    private const int DefaultMaxSize = 256;

    private readonly object _sync = new();
    private readonly Func<T> _create;
    private readonly Action<T> _reset;
    private readonly SwiftStack<T> _available;
    private readonly int _maxSize;

    public GuidePool(Func<T> create, Action<T> reset)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _reset = reset ?? throw new ArgumentNullException(nameof(reset));
        _available = new SwiftStack<T>(DefaultInitialCapacity);
        _maxSize = DefaultMaxSize;
    }

    public T Rent()
    {
        lock (_sync)
        {
            if (_available.Count > 0)
                return _available.Pop();
        }

        return _create();
    }

    public void Release(T guide)
    {
        if (guide == null)
            return;

        _reset(guide);

        lock (_sync)
        {
            if (_available.Count < _maxSize)
                _available.Push(guide);
        }
    }

    public void Destroy(T guide)
    {
        if (guide != null)
            _reset(guide);
    }

    public void Clear()
    {
        lock (_sync)
            _available.Clear();
    }
}
