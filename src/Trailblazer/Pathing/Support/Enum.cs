namespace Trailblazer.Pathing
{
    public enum HeuristicMethod
    {
        Manhattan,
        Octile,
        Euclidean
    }

    public enum StraightNeighbors
    {
        West = 0,  //  (-1, 0, 0)
        South = 1,  //  (0, 0, -1)
        East = 2,  //  (0, 0, 1)
        North = 3,  //  (1, 0, 0)
        Below = 16, //  (0, -1, 0)
        Above = 25  //  (0, 1, 0)
    };

    public enum DiagonalNeighbors
    {
        SouthWest = 4,  //  (-1, 0, -1)
        NorthWest = 5,  //  (-1, 0, 1)
        SouthEast = 6,  //  (1, 0, -1)
        NorthEast = 7,  //  (1, 0, 1)
        BelowWest = 8,  //  (-1, -1, 0)
        BelowSouth = 9,  //  (0, -1, -1)
        BelowEast = 10, //  (0, -1, 1)
        BelowNorth = 11, //  (1, -1, 0)
        BelowSouthWest = 12, //  (-1, -1, -1)
        BelowNorthWest = 13, //  (-1, -1, 1)
        BelowSouthEast = 14, //  (1, -1, -1)
        BelowNorthEast = 15, //  (1, -1, 1)
        AboveWest = 17, //  (-1, 1, 0)
        AboveSouth = 18, //  (0, 1, -1)
        AboveEast = 19, //  (0, 1, 1)
        AboveNorth = 20, //  (1, 1, 0)
        AboveSouthWest = 21, //  (-1, 1, -1)
        AboveNorthWest = 22, //  (-1, 1, 1)
        AboveSouthEast = 23, //  (1, 1, -1)
        AboveNorthEast = 24, //  (1, 1, 1)
    };
}
