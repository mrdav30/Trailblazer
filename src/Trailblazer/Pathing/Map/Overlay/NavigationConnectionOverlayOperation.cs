//=======================================================================
// NavigationConnectionOverlayOperation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the final-state action for a source-owned connection.</summary>
public enum NavigationConnectionOverlayOperationKind
{
    /// <summary>Write or replace a complete connection definition.</summary>
    Upsert = 0,

    /// <summary>Tombstone the effective source-owned connection.</summary>
    Suppress = 1,

    /// <summary>Remove the override or tombstone and restore the baked definition.</summary>
    RevertToBake = 2
}

/// <summary>Describes one immutable source-owned connection overlay operation.</summary>
public readonly struct NavigationConnectionOverlayOperation
{
    private NavigationConnectionOverlayOperation(
        string id,
        NavigationConnectionOverlayOperationKind kind,
        NavigationConnection? connection)
    {
        SwiftThrowHelper.ThrowIfNull(id, nameof(id));
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(id),
            nameof(id),
            "Connection id cannot be empty or whitespace.");

        Id = id;
        Kind = kind;
        Connection = connection;
    }

    /// <summary>Gets the stable source-map-local connection identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the final-state operation kind.</summary>
    public NavigationConnectionOverlayOperationKind Kind { get; }

    /// <summary>
    /// Gets the complete definition for <see cref="NavigationConnectionOverlayOperationKind.Upsert"/>.
    /// The value is ignored for suppression and reversion.
    /// </summary>
    public NavigationConnection? Connection { get; }

    /// <summary>Creates an Upsert operation from a complete connection definition.</summary>
    public static NavigationConnectionOverlayOperation Upsert(NavigationConnection connection)
    {
        SwiftThrowHelper.ThrowIfNull(connection, nameof(connection));
        return new(connection.Id, NavigationConnectionOverlayOperationKind.Upsert, connection);
    }

    /// <summary>Creates a source-owned connection tombstone.</summary>
    public static NavigationConnectionOverlayOperation Suppress(string id) =>
        new(id, NavigationConnectionOverlayOperationKind.Suppress, default);

    /// <summary>Creates an operation that restores the baked source-owned connection.</summary>
    public static NavigationConnectionOverlayOperation RevertToBake(string id) =>
        new(id, NavigationConnectionOverlayOperationKind.RevertToBake, default);

    internal static void ValidateKind(NavigationConnectionOverlayOperationKind kind)
    {
        SwiftThrowHelper.ThrowIfArgument(
            kind is < NavigationConnectionOverlayOperationKind.Upsert or > NavigationConnectionOverlayOperationKind.RevertToBake,
            nameof(kind),
            "Unknown connection overlay operation kind.");
    }
}
