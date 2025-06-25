namespace CityBuilderCore
{
    /// <summary>
    /// query that eventually produces a <see cref="WorkerPath"/><br/>
    /// enables distribution of calculations across frames
    /// </summary>
    public abstract class WorkerPathQuery
    {
        /// <summary>
        /// forces the query to produce its result
        /// </summary>
        /// <returns>the calculated path</returns>
        public abstract WorkerPath Complete();
    }
}