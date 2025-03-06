using FixedMathSharp;

namespace Trailblazer
{
    internal static class Extensions
    {
        public static Vector3d ClampMagnitude(this Vector3d value, Fixed64 maxMagnitude)
        {
            Fixed64 magnitudeSqr = value.SqrMagnitude;
            if (magnitudeSqr > maxMagnitude * maxMagnitude)
            {
                Fixed64 magnitude = FixedMath.Sqrt(magnitudeSqr); // Get actual magnitude
                return (value / magnitude) * maxMagnitude; // Scale vector to max magnitude
            }
            return value;
        }
    }
}
