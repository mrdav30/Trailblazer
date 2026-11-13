//=======================================================================
// Navigator2DTrekRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler;
using FixedMathSharp;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>Records a planar destination and its acquisition options independently of any guide.</summary>
internal struct Navigator2DTrekRecord : IRecordable
{
    private const int CurrentSchemaVersion = 2;

    internal NavigationCellAddress Source;
    internal NavigationCellAddress Destination;
    internal Fixed64 ArrivalRadius;
    internal NavigationAreaPolicyKey Policy;
    internal NavigationWorkBudget Budget;
    internal PathAlgorithm Algorithm;
    internal int MovementGroupId;
    internal bool AllowTransitions;
    internal bool Arrived;

    public void RecordData(IChronicler chronicler)
    {
        bool loading = chronicler.Mode == SerializationMode.Loading;
        int schema = loading ? 0 : CurrentSchemaVersion;
        RecordValues.Look(chronicler, ref schema, "SchemaVersion", 0);
        ChronicleGuard.ThrowIfTrue(schema != CurrentSchemaVersion, message: "Serialized planar trek schema is missing or unsupported.");
        LookAddress(chronicler, ref Source, "Source");
        LookAddress(chronicler, ref Destination, "Destination");
        RecordValues.Look(chronicler, ref ArrivalRadius, "ArrivalRadius", Fixed64.Zero);
        string? policy = Policy.PolicyId;
        long revision = Policy.Revision;
        RecordValues.Look(chronicler, ref policy, "Policy", null);
        RecordValues.Look(chronicler, ref revision, "PolicyRevision", 0L);
        RecordValues.Look(chronicler, ref Algorithm, "Algorithm", PathAlgorithm.AStar);
        RecordValues.Look(chronicler, ref MovementGroupId, "MovementGroupId", -1);
        RecordValues.Look(chronicler, ref AllowTransitions, "AllowTransitions", false);
        RecordValues.Look(chronicler, ref Arrived, "Arrived", false);
        var budget = new NavigationWorkBudgetRecord(Budget);
        budget.Look(chronicler);
        if (loading)
        {
            Budget = budget.CreateBudget();
            Policy = new NavigationAreaPolicyKey(policy!, revision);
        }
    }

    private static void LookAddress(IChronicler chronicler, ref NavigationCellAddress address, string prefix)
    {
        string? map = address.MapId;
        var (x, y, z) = (address.Index.x, address.Index.y, address.Index.z);
        RecordValues.Look(chronicler, ref map, prefix + "Map", null);
        RecordValues.Look(chronicler, ref x, prefix + "X", 0);
        RecordValues.Look(chronicler, ref y, prefix + "Y", 0);
        RecordValues.Look(chronicler, ref z, prefix + "Z", 0);
        if (chronicler.Mode == SerializationMode.Loading)
            address = map == null ? default : new NavigationCellAddress(map, new VoxelIndex(x, y, z));
    }
}
