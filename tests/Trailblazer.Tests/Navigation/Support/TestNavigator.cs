using FluentAssertions;
using FixedMathSharp;
using Trailblazer.Navigation;
using Xunit;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;

namespace Trailblazer.Tests.Navigation
{
    //// A tiny concrete subclass so we can test the abstract Navigator
    //public class TestNavigator : Navigator
    //{
    //    public bool TraverseCalled;
    //    public TraversalRequest LastTraverseRequest;
    //    public bool SteeringSimulated;

    //    public TestNavigator()
    //    {
    //        // stub out Steering & Motor so we can observe calls
    //        Steering = new StubSteering(this);
    //        Motor = new StubMotor(this);
    //    }

    //    public override void CheckTraversalCondition() { }

    //    private class StubSteering : NavSteering
    //    {
    //        private readonly TestNavigator _parent;
    //        public StubSteering(TestNavigator parent) : base(parent)
    //            => _parent = parent;
    //        public override void OnSimulate(ISteer body)
    //            => _parent.SteeringSimulated = true;
    //    }

    //    private class StubMotor : NavMotor
    //    {
    //        private readonly TestNavigator _parent;
    //        public StubMotor(TestNavigator parent)
    //            : base(parent, TraversalCondition.Empty)
    //            => _parent = parent;
    //        public override void Traverse(TraversalRequest request)
    //        {
    //            _parent.TraverseCalled = true;
    //            _parent.LastTraverseRequest = request;
    //        }
    //    }
    //}
}
