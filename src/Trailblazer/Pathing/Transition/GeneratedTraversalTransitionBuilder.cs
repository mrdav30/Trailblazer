using FixedMathSharp;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Builds generated chart-to-volume transitions from chart cell metadata.
/// </summary>
internal static class GeneratedTraversalTransitionBuilder
{
    private static readonly (int Dx, int Dy, int Dz)[] PositivePerpendicularNeighborOffsets =
    {
        (1, 0, 0),
        (0, 1, 0),
        (0, 0, 1)
    };

    internal static TraversalTransition[] BuildTransitions(
        NavigationChart chart,
        string transitionIdPrefix)
    {
        SwiftList<TraversalTransition> transitions = new();
        int[] generatedIndices = chart.GetGeneratedTransitionIndices();
        for (int i = 0; i < generatedIndices.Length; i++)
        {
            int flatIndex = generatedIndices[i];
            chart.DecodeIndex(flatIndex, out int x, out int y, out int z);
            NavigationChartCell currentCell = chart.GetCell(x, y, z);

            for (int neighborOffsetIndex = 0; neighborOffsetIndex < PositivePerpendicularNeighborOffsets.Length; neighborOffsetIndex++)
            {
                (int dx, int dy, int dz) = PositivePerpendicularNeighborOffsets[neighborOffsetIndex];
                int neighborX = x + dx;
                int neighborY = y + dy;
                int neighborZ = z + dz;
                if (!chart.IsInBounds(neighborX, neighborY, neighborZ))
                    continue;

                NavigationChartCell neighborCell = chart.GetCell(neighborX, neighborY, neighborZ);
                if (!neighborCell.CanGenerateTransition)
                    continue;

                if (TryBuildTransitionsForPair(
                    chart,
                    transitionIdPrefix,
                    x,
                    y,
                    z,
                    currentCell,
                    neighborX,
                    neighborY,
                    neighborZ,
                    neighborCell,
                    out TraversalTransition chartToVolumeTransition,
                    out TraversalTransition volumeToChartTransition))
                {
                    transitions.Add(chartToVolumeTransition);
                    transitions.Add(volumeToChartTransition);
                }
            }
        }

        return transitions.ToArray();
    }

    internal static TraversalTransition[] BuildTransitionsForPair(
        NavigationChart chart,
        string transitionIdPrefix,
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ)
    {
        if (!chart.IsInBounds(firstX, firstY, firstZ)
            || !chart.IsInBounds(secondX, secondY, secondZ))
        {
            return System.Array.Empty<TraversalTransition>();
        }

        NavigationChartCell firstCell = chart.GetCell(firstX, firstY, firstZ);
        NavigationChartCell secondCell = chart.GetCell(secondX, secondY, secondZ);
        if (!firstCell.CanGenerateTransition || !secondCell.CanGenerateTransition)
            return System.Array.Empty<TraversalTransition>();

        return TryBuildTransitionsForPair(
            chart,
            transitionIdPrefix,
            firstX,
            firstY,
            firstZ,
            firstCell,
            secondX,
            secondY,
            secondZ,
            secondCell,
            out TraversalTransition chartToVolumeTransition,
            out TraversalTransition volumeToChartTransition)
            ? new[] { chartToVolumeTransition, volumeToChartTransition }
            : System.Array.Empty<TraversalTransition>();
    }

    internal static string[] GetPotentialTransitionIdsForPair(
        string transitionIdPrefix,
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ)
    {
        return new[]
        {
            CreateGeneratedTransitionId(transitionIdPrefix, TraversalTransitionType.Takeoff, firstX, firstY, firstZ, secondX, secondY, secondZ),
            CreateGeneratedTransitionId(transitionIdPrefix, TraversalTransitionType.Takeoff, secondX, secondY, secondZ, firstX, firstY, firstZ),
            CreateGeneratedTransitionId(transitionIdPrefix, TraversalTransitionType.Landing, firstX, firstY, firstZ, secondX, secondY, secondZ),
            CreateGeneratedTransitionId(transitionIdPrefix, TraversalTransitionType.Landing, secondX, secondY, secondZ, firstX, firstY, firstZ),
            CreateGeneratedTransitionId(transitionIdPrefix, TraversalTransitionType.SwimEntry, firstX, firstY, firstZ, secondX, secondY, secondZ),
            CreateGeneratedTransitionId(transitionIdPrefix, TraversalTransitionType.SwimEntry, secondX, secondY, secondZ, firstX, firstY, firstZ),
            CreateGeneratedTransitionId(transitionIdPrefix, TraversalTransitionType.SwimExit, firstX, firstY, firstZ, secondX, secondY, secondZ),
            CreateGeneratedTransitionId(transitionIdPrefix, TraversalTransitionType.SwimExit, secondX, secondY, secondZ, firstX, firstY, firstZ)
        };
    }

    private static bool TryBuildTransitionsForPair(
        NavigationChart chart,
        string transitionIdPrefix,
        int firstX,
        int firstY,
        int firstZ,
        NavigationChartCell firstCell,
        int secondX,
        int secondY,
        int secondZ,
        NavigationChartCell secondCell,
        out TraversalTransition chartToVolumeTransition,
        out TraversalTransition volumeToChartTransition)
    {
        chartToVolumeTransition = default;
        volumeToChartTransition = default;

        if (!TryResolveSingleBoundaryCandidate(
            firstCell.GeneratedTransitionMedia,
            secondCell.GeneratedTransitionMedia,
            chart.GetWorldPosition(firstX, firstY, firstZ),
            chart.GetWorldPosition(secondX, secondY, secondZ),
            out Vector3d chartPosition,
            out Vector3d volumePosition,
            out TraversalMedium volumeMedium))
        {
            return false;
        }

        return TryBuildChartVolumeTransitionPair(
            chart,
            transitionIdPrefix,
            chartPosition,
            volumePosition,
            volumeMedium,
            out chartToVolumeTransition,
            out volumeToChartTransition);
    }

    private static bool TryResolveSingleBoundaryCandidate(
        TraversalMedia firstTransitionMedia,
        TraversalMedia secondTransitionMedia,
        Vector3d firstPosition,
        Vector3d secondPosition,
        out Vector3d chartPosition,
        out Vector3d volumePosition,
        out TraversalMedium volumeMedium)
    {
        chartPosition = default;
        volumePosition = default;
        volumeMedium = TraversalMedium.Unknown;

        int candidateCount = 0;

        TryAddChartVolumeCandidate(
            firstTransitionMedia,
            secondTransitionMedia,
            TraversalMedium.Gas,
            firstPosition,
            secondPosition,
            ref candidateCount,
            ref chartPosition,
            ref volumePosition,
            ref volumeMedium);
        TryAddChartVolumeCandidate(
            firstTransitionMedia,
            secondTransitionMedia,
            TraversalMedium.Liquid,
            firstPosition,
            secondPosition,
            ref candidateCount,
            ref chartPosition,
            ref volumePosition,
            ref volumeMedium);

        return candidateCount == 1;
    }

    private static void TryAddChartVolumeCandidate(
        TraversalMedia firstTransitionMedia,
        TraversalMedia secondTransitionMedia,
        TraversalMedium candidateVolumeMedium,
        Vector3d firstPosition,
        Vector3d secondPosition,
        ref int candidateCount,
        ref Vector3d chartPosition,
        ref Vector3d volumePosition,
        ref TraversalMedium volumeMedium)
    {
        TraversalMedia candidateVolumeKind = candidateVolumeMedium switch
        {
            TraversalMedium.Gas => TraversalMedia.Gas,
            TraversalMedium.Liquid => TraversalMedia.Liquid,
            _ => TraversalMedia.None
        };

        if (candidateVolumeKind == TraversalMedia.None)
            return;

        bool firstCanBeChart = (firstTransitionMedia & TraversalMedia.Solid) != 0;
        bool secondCanBeChart = (secondTransitionMedia & TraversalMedia.Solid) != 0;
        bool firstCanBeVolume = (firstTransitionMedia & candidateVolumeKind) != 0;
        bool secondCanBeVolume = (secondTransitionMedia & candidateVolumeKind) != 0;

        if (firstCanBeChart && secondCanBeVolume)
        {
            candidateCount++;
            chartPosition = firstPosition;
            volumePosition = secondPosition;
            volumeMedium = candidateVolumeMedium;
        }

        if (secondCanBeChart && firstCanBeVolume)
        {
            candidateCount++;
            chartPosition = secondPosition;
            volumePosition = firstPosition;
            volumeMedium = candidateVolumeMedium;
        }
    }

    private static bool TryBuildChartVolumeTransitionPair(
        NavigationChart chart,
        string transitionIdPrefix,
        Vector3d chartPosition,
        Vector3d volumePosition,
        TraversalMedium volumeMedium,
        out TraversalTransition chartToVolumeTransition,
        out TraversalTransition volumeToChartTransition)
    {
        chartToVolumeTransition = default;
        volumeToChartTransition = default;

        TraversalTransitionType entryType;
        TraversalTransitionType exitType;
        TraversalTransitionAnchor volumeAnchor;

        switch (volumeMedium)
        {
            case TraversalMedium.Gas:
                entryType = TraversalTransitionType.Takeoff;
                exitType = TraversalTransitionType.Landing;
                volumeAnchor = TraversalTransitionAnchor.Gas(volumePosition);
                break;
            case TraversalMedium.Liquid:
                entryType = TraversalTransitionType.SwimEntry;
                exitType = TraversalTransitionType.SwimExit;
                volumeAnchor = TraversalTransitionAnchor.Liquid(volumePosition);
                break;
            default:
                return false;
        }

        chart.TryWorldToIndex(chartPosition, out int chartX, out int chartY, out int chartZ);
        chart.TryWorldToIndex(volumePosition, out int volumeX, out int volumeY, out int volumeZ);

        TraversalTransitionAnchor chartAnchor = TraversalTransitionAnchor.Solid(chartPosition);
        chartToVolumeTransition = new TraversalTransition(
            CreateGeneratedTransitionId(
                transitionIdPrefix,
                entryType,
                chartX,
                chartY,
                chartZ,
                volumeX,
                volumeY,
                volumeZ),
            entryType,
            chartAnchor,
            volumeAnchor);
        volumeToChartTransition = new TraversalTransition(
            CreateGeneratedTransitionId(
                transitionIdPrefix,
                exitType,
                volumeX,
                volumeY,
                volumeZ,
                chartX,
                chartY,
                chartZ),
            exitType,
            volumeAnchor,
            chartAnchor);
        return true;
    }

    private static string CreateGeneratedTransitionId(
        string transitionIdPrefix,
        TraversalTransitionType transitionType,
        int sourceX,
        int sourceY,
        int sourceZ,
        int destinationX,
        int destinationY,
        int destinationZ)
    {
        return
            $"{transitionIdPrefix}:{transitionType}:{sourceY}_{sourceX}_{sourceZ}->{destinationY}_{destinationX}_{destinationZ}";
    }
}
