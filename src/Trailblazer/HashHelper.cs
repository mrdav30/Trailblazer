using System.Runtime.CompilerServices;

namespace Trailblazer
{
    public static class HashHelper
    {
        // System.String.GetHashCode(): http://referencesource.microsoft.com/#mscorlib/system/string.cs,0a17bbac4851d0d4
        // System.Web.Util.StringUtil.GetStringHashCode(System.String): http://referencesource.microsoft.com/#System.Web/Util/StringUtil.cs,c97063570b4e791a
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
