//=======================================================================
// NavigationOperationCandidate.ExplicitConnectionRefreshWork.IncidenceOwnerTree.Node.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Contains the Node companion type.</content>
internal sealed partial class NavigationOperationCandidate
{
    internal sealed partial class ExplicitConnectionRefreshWork
    {
        private partial struct IncidenceOwnerTree
        {
            /// <summary>Stores one target-owner entry in the persistent incidence tree.</summary>
            internal sealed class Node
            {
                internal Node(NavigationConnectionOwnerKey owner, Node? parent)
                {
                    Owner = owner;
                    Parent = parent;
                }

                internal NavigationConnectionOwnerKey Owner { get; }

                internal Node? Parent { get; set; }

                internal Node? Left { get; set; }

                internal Node? Right { get; set; }

                internal int Height { get; set; } = 1;
            }
        }
    }
}
