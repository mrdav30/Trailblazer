//=======================================================================
// NavigationProfileReplacementCommit.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Navigation;

/// <summary>Commits a validated physical profile and synchronously publishes its navigation state.</summary>
/// <typeparam name="TProfile">The dimensional navigation profile.</typeparam>
/// <typeparam name="TPosition">The dimensional body-root position.</typeparam>
/// <param name="replacement">The validated replacement profile.</param>
/// <param name="position">The root preserving the current foot.</param>
/// <param name="publishNavigationState">Invoked once after physical commitment and before notifications.</param>
/// <param name="notificationException">Post-commit notification failures, if any.</param>
/// <returns>Applied, PhysicalFitRejected, or PhysicalFitUnavailable.</returns>
public delegate NavigationProfileReplacementStatus NavigationProfileReplacementCommit<TProfile, TPosition>(
    TProfile replacement, TPosition position, Action publishNavigationState, out Exception? notificationException);
