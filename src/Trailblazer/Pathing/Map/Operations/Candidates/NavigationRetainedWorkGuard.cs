//=======================================================================
// NavigationRetainedWorkGuard.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Checks whether staged navigation work fits the retained byte and page limits.</summary>
internal delegate bool NavigationRetainedWorkGuard(long retainedBytes, int persistentPages);
