using System;
using System.Collections;

namespace Trailblazer.Utility.Coroutines
{
    /// <summary>
    /// Repurposed from Unity's CustomYieldInstruction
    /// </summary>
    public abstract class LockedYieldInstruction : IEnumerator, IDisposable
    {
        /// <summary>
        /// Indicates if coroutine should be kept suspended.
        /// </summary>
        public abstract bool KeepWaiting { get; }

        public object Current => null;

        public bool MoveNext() => KeepWaiting;

        public void Reset() { }

        public virtual void Dispose() { }
    }
}