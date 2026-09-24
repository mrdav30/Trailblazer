using Chronicler;
using Chronicler.Serialization;

namespace Trailblazer.Gravitas.Tests.Support;

internal static class GravitasRecordSnapshot
{
    internal static object Save(IRecordable source, bool memoryPack)
    {
#if !TRAILBLAZER_DISABLE_MEMORYPACK
        if (memoryPack)
            return MemoryPackRecordSerializer.Serialize(source);
#endif
        return JsonRecordSerializer.Serialize(source, writeIndented: true);
    }

    internal static void Load(IRecordable target, object snapshot)
    {
#if !TRAILBLAZER_DISABLE_MEMORYPACK
        if (snapshot is byte[] bytes)
        {
            MemoryPackRecordSerializer.Populate(target, bytes);
            return;
        }
#endif
        JsonRecordSerializer.Populate(target, (string)snapshot);
    }
}
