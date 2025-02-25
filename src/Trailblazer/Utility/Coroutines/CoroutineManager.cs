using SwiftCollections;
using System;
using System.Collections.Generic;

namespace Trailblazer.Utility.Coroutines
{
    public static class CoroutineManager
    {
        private static SwiftBucket<LSCoroutine> Coroutines;

        public static void Initialize()
        {
            Coroutines ??= new SwiftBucket<LSCoroutine>();
            Coroutines.Clear();
        }

        public static void Simulate()
        {
            for (int i = 0; i < Coroutines.PeakCount; i++)
            {
                if (Coroutines.IsAllocated(i))
                {
                    LSCoroutine coroutine = Coroutines[i];
                    if (coroutine.Active)
                        coroutine.Simulate();
                }
            }
        }

        /// <summary>
        /// Starts coroutine that returns number of frames to wait.
        /// </summary>
        /// <returns>The coroutine.</returns>
        /// <param name="enumerator">Enumerator.</param>
        public static LSCoroutine StartCoroutine(IEnumerator<LockedYieldInstruction> enumerator)
        {
            LSCoroutine coroutine = new();
            coroutine.Initialize(enumerator);
            coroutine.Index = Coroutines.Add(coroutine);
            return coroutine;
        }

        public static void StopCoroutine(LSCoroutine coroutine)
        {
            if (coroutine.Active == false)
                Console.WriteLine("Coroutine already stopped");

            Coroutines.TryRemoveAt(coroutine.Index);
            coroutine.Active = false;
            coroutine.End();
        }

        public static void Deactivate()
        {
            Coroutines.Clear();
        }
    }
}