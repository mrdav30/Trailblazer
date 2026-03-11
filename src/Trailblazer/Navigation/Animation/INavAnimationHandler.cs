using FixedMathSharp;

namespace Trailblazer.Navigation.Animation;

public interface INavAnimationHandler
{
    void SetDirectionalInput(Fixed64 forward, Fixed64 sideways, Fixed64 dampTime);
    void SetIsSprinting(bool isSprinting);
    void ApplyRootMotion(Vector3d deltaPosition, Fixed64 forceMultiplier);
}
