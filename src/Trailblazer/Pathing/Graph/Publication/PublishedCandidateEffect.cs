//=======================================================================
// PublishedCandidateEffect.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies which graph publication work the candidate affected.</summary>
internal enum PublishedCandidateEffect : byte
{
    InstallMaterialized,
    CompleteMaterialized,
    CompleteMaterializedWithinStructuralPublication,
    CompleteAutomaticSeam,
    CompleteComposition,
    ReopenStructuralScopes,
    StartAutomaticSeam
}
