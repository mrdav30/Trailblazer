namespace Trailblazer.Pathing
{
    public enum HeuristicMethod
    {
        Manhattan,
        Octile,
        Euclidean
        //Chebyshev?
    }

    public enum PerpendicularDirections
    {
        West = 0,   //  (-1, 0, 0)
        South = 1,  //  (0, 0, -1)
        East = 2,   //  (0, 0, 1)
        North = 3,  //  (1, 0, 0)
        Below = 4,  //  (0, -1, 0)
        Above = 5   //  (0, 1, 0)
    };

    public enum DiagonalDirections
    {
        SouthWest = 6,          //  (-1, 0, -1)
        NorthWest = 7,          //  (-1, 0, 1)
        SouthEast = 8,          //  (1, 0, -1)
        NorthEast = 9,          //  (1, 0, 1)
        BelowWest = 10,         //  (-1, -1, 0)
        BelowSouth = 11,        //  (0, -1, -1)
        BelowEast = 12,         //  (0, -1, 1)
        BelowNorth = 13,        //  (1, -1, 0)
        AboveWest = 14,         //  (-1, 1, 0)
        AboveSouth = 15,        //  (0, 1, -1)
        AboveEast = 16,         //  (0, 1, 1)
        AboveNorth = 17,        //  (1, 1, 0)
        BelowSouthWest = 18,    //  (-1, -1, -1)
        BelowNorthWest = 19,    //  (-1, -1, 1)
        BelowSouthEast = 20,    //  (1, -1, -1)
        BelowNorthEast = 21,    //  (1, -1, 1)
        AboveSouthWest = 22,    //  (-1, 1, -1)
        AboveNorthWest = 23,    //  (-1, 1, 1)
        AboveSouthEast = 24,    //  (1, 1, -1)
        AboveNorthEast = 25,    //  (1, 1, 1)
    };
}
