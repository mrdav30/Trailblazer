using System;
using System.Collections.Generic;
using SwiftCollections;

namespace Trailblazer.Shared.Utility
{
    public static class CollectionExtensions
    {
        /// <summary>
        /// Gets the element at the specified index from the end (1-based).
        /// For example, FromEnd(1) returns the last item, FromEnd(2) returns second-to-last.
        /// </summary>
        public static T FromEnd<T>(this IEnumerable<T> source, int reverseIndex)
        {
            if (source is IList<T> list)
                return list.FromEnd(reverseIndex);

            if (source is SwiftList<T> swift)
                return swift.FromEnd(reverseIndex);

            // fallback for generic IEnumerable
            var buffer = new List<T>(source);
            return buffer.FromEnd(reverseIndex);
        }

        /// <summary>
        /// Gets the element at the specified index from the end (1-based) from IList.
        /// </summary>
        public static T FromEnd<T>(this IList<T> list, int reverseIndex)
        {
            if (reverseIndex <= 0 || reverseIndex > list.Count)
                throw new ArgumentOutOfRangeException(nameof(reverseIndex));
            return list[list.Count - reverseIndex];
        }

        /// <summary>
        /// Gets the element at the specified index from the end (1-based) from SwiftList.
        /// </summary>
        public static T FromEnd<T>(this SwiftList<T> list, int reverseIndex)
        {
            if (reverseIndex <= 0 || reverseIndex > list.Count)
                throw new ArgumentOutOfRangeException(nameof(reverseIndex));
            return list[list.Count - reverseIndex];
        }

        /// <summary>
        /// Returns the last item in the sequence.
        /// </summary>
        public static T Last<T>(this IEnumerable<T> source) => source.FromEnd(1);

        /// <summary>
        /// Returns the second-to-last item in the sequence.
        /// </summary>
        public static T SecondToLast<T>(this IEnumerable<T> source) => source.FromEnd(2);
    }
}
