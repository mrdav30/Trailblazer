using System.Collections.Generic;

namespace Trailblazer.Utility.Coroutines
{
    public class LSCoroutine
    {
        public IEnumerator<LockedYieldInstruction> Enumerator;
        public bool Active = true;
        public int Index;

        public void Initialize(IEnumerator<LockedYieldInstruction> enumerator)
        {
            Enumerator = enumerator;
            Active = true;
        }

        public void Simulate()
        {
            if (Enumerator.Current != null && Enumerator.Current.KeepWaiting)
                return;

            if (Enumerator.MoveNext())
                return;
            else
                CoroutineManager.StopCoroutine(this);
        }
        public void End()
        {
            Active = false;
            Enumerator.Dispose();
        }
    }
}