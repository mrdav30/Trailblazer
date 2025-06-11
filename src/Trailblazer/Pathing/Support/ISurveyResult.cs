namespace Trailblazer.Pathing
{
    public interface ISurveyResult
    {
        /// <summary>
        /// Indicates whether the result is currently valid and can be used.
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        /// Indicates whether the result is currently in use by an agent.
        /// </summary>
        public bool IsInUse { get; }

        /// <summary>
        /// The frame in which this result was last used, used for eviction or reuse logic.
        /// </summary>
        public int LastUsedFrame { get; }

        /// <summary>
        /// A unique hash key representing the request that generated this result.
        /// </summary>
        int RequestHashKey { get; }

        /// <summary>
        /// Marks the result as in use for the current frame or request.
        /// </summary>
        void MarkInUse();

        /// <summary>
        /// Releases the result for reuse or reinitialization.
        /// </summary>
        void Release();
    }
}
