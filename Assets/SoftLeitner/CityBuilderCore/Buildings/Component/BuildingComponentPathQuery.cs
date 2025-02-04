namespace CityBuilderCore
{
    /// <summary>
    /// query that eventually produces a <see cref="BuildingComponentPath{T}"/><br/>
    /// for example in paths leading to item givers or receivers
    /// enables distribution of path calculations across frames
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class BuildingComponentPathQuery<T> where T : IBuildingComponent
    {
        /// <summary>
        /// signals to the query that the path is no longer needed
        /// </summary>
        public abstract void Cancel();
        /// <summary>
        /// forces the query to produce its result
        /// </summary>
        /// <returns>the calculated path</returns>
        public abstract BuildingComponentPath<T> Complete();
    }
}