using System.Globalization;
using System.Text;
using FixedMathSharp;
using Xunit;

namespace Trailblazer.Gravitas.Tests.Support;

internal static class GravitasMotionTrace
{
    internal static void WriteFrame(ITestOutputHelper output, int frame, params Vector3d[] vectors)
    {
        var line = new StringBuilder(frame.ToString(CultureInfo.InvariantCulture));
        foreach (Vector3d vector in vectors)
        {
            line.Append('|').Append(vector.X.ToRawString())
                .Append(',').Append(vector.Y.ToRawString())
                .Append(',').Append(vector.Z.ToRawString());
        }
        output.WriteLine(line.ToString());
    }
}
