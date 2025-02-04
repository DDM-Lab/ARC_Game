namespace CityBuilderCore
{
    /// <summary>
    /// query that eventually produces a <see cref="WalkingPath"/><br/>
    /// enables distribution of path calculations across frames
    /// </summary>
    public abstract class PathQuery
    {
        public const int DEFAULT_MAX_CALCULATIONS = 16;

        /// <summary>
        /// can be used to transport additional data when needed
        /// </summary>
        public object ExtraData {  get; set; }

        /// <summary>
        /// signals to the query that the path is no longer needed
        /// </summary>
        public abstract void Cancel();
        /// <summary>
        /// forces the query to produce its result
        /// </summary>
        /// <returns>the calculated path</returns>
        public abstract WalkingPath Complete();
    }
}
