using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SwiftCollections;

namespace Trailblazer
{
    public static class CollectionExtensions
    {
        /// <summary>
        /// Gets the element at the specified index from the end (1-based).
        /// For example, FromEnd(1) returns the last item, FromEnd(2) returns second-to-last.
        /// </summary>
        public static T FromEnd<T>(this IEnumerable<T> source, int reverseIndex)
        {
            if (source is SwiftList<T> swift)
                return swift.FromEnd(reverseIndex);

            // fallback for generic IEnumerable
            var buffer = new List<T>(source);
            return buffer.FromEnd(reverseIndex);
        }

        /// <summary>
        /// Gets the element at the specified index from the end (1-based) from SwiftList.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T FromEnd<T>(this SwiftList<T> list, int reverseIndex)
        {
            if (reverseIndex <= 0 || reverseIndex > list.Count)
                throw new ArgumentOutOfRangeException(nameof(reverseIndex));
            return list[list.Count - reverseIndex];
        }

        /// <summary>
        /// Returns the last item in the sequence.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Last<T>(this IEnumerable<T> source) => source.FromEnd(1);
    }
}
