//=======================================================================
// EndpointEnumerator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Enumerates the automatic seam references for one endpoint.</summary>
internal struct EndpointEnumerator
{
    private readonly NavigationCellAddress _origin;
    private NavigationPagedSequence<NavigationAutomaticSeamPair>.Enumerator _pairs;

    internal EndpointEnumerator(
        NavigationCellAddress origin,
        NavigationPagedSequence<NavigationAutomaticSeamPair> pairs)
    {
        _origin = origin;
        _pairs = pairs.GetEnumerator();
        Current = default;
    }

    internal NavigationAutomaticSeamRef Current { get; private set; }

    internal bool MoveNext()
    {
        if (!_pairs.MoveNext())
        {
            Current = default;
            return false;
        }
        NavigationAutomaticSeamPair pair = _pairs.Current;
        Current = new NavigationAutomaticSeamRef(
            pair,
            reverse: _origin.Equals(pair.Second));
        return true;
    }
}
