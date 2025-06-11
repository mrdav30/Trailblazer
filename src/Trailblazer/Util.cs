using System.Runtime.CompilerServices;

namespace Trailblazer
{
    internal static class Util
    {
        /// <summary>
        /// Clamp a value to the inclusive range [min, max].
        /// </summary>
        /// <remarks>
        /// In newer versions of the .NET Framework, there is a System.Math.Clamp() method. 
        /// </remarks>
        /// <typeparam name="T">The type of value.</typeparam>
        /// <param name="value">The value to clamp.</param>
        /// <param name="min">The minimum value.</param>
        /// <param name="max">The maximum value.</param>
        /// <returns>The clamped value.</returns>
        public static T Clamp<T>(T value, T min, T max) where T : System.IComparable<T>
        {
            if (value.CompareTo(max) > 0)
                return max;

            if (value.CompareTo(min) < 0)
                return min;

            return value;
        }

        public static int CombineHashCodes(
            this ITuple tupled,
            int seed = 5381,
            int shift1 = 16,
            int shift2 = 5,
            int shift3 = 27,
            int factor3 = 1566083941)
        {
            int hash1 = (seed << shift1) + seed;
            int hash2 = hash1;

            for (int i = 0; i < tupled.Length; i++)
            {
                unchecked
                {
                    if (i % 2 == 0)
                        hash1 = ((hash1 << shift2) + hash1 + (hash1 >> shift3)) ^ tupled[i].GetHashCode();
                    else
                        hash2 = ((hash2 << shift2) + hash2 + (hash2 >> shift3)) ^ tupled[i].GetHashCode();
                }
            }

            return hash1 + (hash2 * factor3);
        }
    }
}
