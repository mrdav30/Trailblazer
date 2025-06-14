using FixedMathSharp;
using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Numeric;

// TODO: move these into FixedMathSharp
namespace Trailblazer.Tests
{
    public class Fixed64Assertions : ComparableTypeAssertions<Fixed64, Fixed64Assertions>
    {
        public Fixed64Assertions(Fixed64 value) : base(value, AssertionChain.GetOrCreate()) { }

        protected override string Identifier => "fixed64";

        public AndConstraint<Fixed64Assertions> BeApproximately(
            Fixed64 expected, 
            Fixed64? tolerance = null, 
            string because = "", params object[] becauseArgs)
        {
            Fixed64 limit = tolerance ?? Fixed64.Epsilon;

            this.CurrentAssertionChain
                .ForCondition(((Fixed64)Subject - expected).Abs() <= limit)
                .BecauseOf(because, becauseArgs)
                .FailWith($"Expected {Subject} to be approximately {expected} ± {limit}, but found {Subject}.");

            return new AndConstraint<Fixed64Assertions>(this);
        }
    }

    public static class Fixed64AssertionsExtensions
    {
        public static Fixed64Assertions Should(this Fixed64 actualValue)
        {
            return new Fixed64Assertions(actualValue);
        }
    }

    public class Vector3dAssertions : ComparableTypeAssertions<Vector3d, Vector3dAssertions>
    {
        public Vector3dAssertions(Vector3d value) : base(value, AssertionChain.GetOrCreate()) { }

        protected override string Identifier => "vector3d";

        public AndConstraint<Vector3dAssertions> BeApproximately(
            Vector3d expected, 
            Fixed64? tolerance = null, 
            string because = "", params object[] becauseArgs)
        {
            Fixed64 limit = tolerance ?? Fixed64.Epsilon;

            this.CurrentAssertionChain
                .ForCondition(((Vector3d)Subject - expected).Magnitude <= limit)
                .BecauseOf(because, becauseArgs)
                .FailWith($"Expected {Subject} to be approximately {expected} ± {limit}, but found {Subject}.");

            return new AndConstraint<Vector3dAssertions>(this);
        }

        public AndConstraint<Vector3dAssertions> NotBeApproximately(
            Vector3d expected, 
            Fixed64? tolerance = null, 
            string because = "", 
            params object[] becauseArgs)
        {
            Fixed64 limit = tolerance ?? Fixed64.Epsilon;

            this.CurrentAssertionChain
                .ForCondition(((Vector3d)Subject - expected).Magnitude >= limit)
                .BecauseOf(because, becauseArgs)
                .FailWith($"Expected {Subject} to <c>not</c> be approximately {expected} ± {limit}, but found {Subject}.");

            return new AndConstraint<Vector3dAssertions>(this);
        }

        public AndConstraint<Vector3dAssertions> HaveComponentApproximately(
            Vector3d expected, 
            Fixed64? tolerance = null, 
            string because = "", params object[] becauseArgs)
        {
            Fixed64 limit = tolerance ?? Fixed64.Epsilon;

            Vector3d subjectVector = (Vector3d)Subject;
            this.CurrentAssertionChain
                .ForCondition((subjectVector.x - expected.x).Abs() <= limit
                           && (subjectVector.y - expected.y).Abs() <= limit
                           && (subjectVector.z - expected.z).Abs() <= limit)
                .BecauseOf(because, becauseArgs)
                .FailWith($"Expected {Subject} components to be approximately {expected} ± {limit}, but found {Subject}.");

            return new AndConstraint<Vector3dAssertions>(this);
        }
    }

    public static class Vector3dAssertionsExtensions
    {
        public static Vector3dAssertions Should(this Vector3d actualValue)
        {
            return new Vector3dAssertions(actualValue);
        }
    }
}