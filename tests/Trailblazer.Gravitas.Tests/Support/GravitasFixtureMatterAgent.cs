using FixedMathSharp;
using Gravitas;

namespace Trailblazer.Gravitas.Tests.Support;

internal sealed class GravitasFixtureMatterAgent : IMatterAgent
{
    internal GravitasFixtureMatterAgent(
        GravitasWorldContext context,
        FixedTransform transform)
    {
        Context = context;
        Transform = transform;
    }

    public GravitasWorldContext Context { get; }

    public FixedTransform Transform { get; }

    public bool IsParent => true;

    public bool IsInteracting => false;
}
