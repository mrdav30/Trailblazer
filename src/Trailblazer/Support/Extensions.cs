using SwiftCollections;

// TODO: move these into SwiftCollections

public static class SwiftCollectionExtensions
{
    public static bool TryGetValue<T>(this SwiftBucket<T> bucket, int key, out T value)
    {
        if (!bucket.IsAllocated(key))
        {
            value = default;
            return false;
        }

        value = bucket[key];
        return true;
    }
}