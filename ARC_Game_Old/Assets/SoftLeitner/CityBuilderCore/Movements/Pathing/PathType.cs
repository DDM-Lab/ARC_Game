namespace CityBuilderCore
{
    /// <summary>
    /// defines which pathfinder will be used
    /// </summary>
    public enum PathType
    {
        /// <summary>
        /// use default pathfinding, register <see cref="IPathfinder"/>
        /// </summary>
        Any = 0,
        /// <summary>
        /// a* pathfinding in roads
        /// </summary>
        Road = 10,
        /// <summary>
        /// a* pathfinding in roads unless blocked
        /// </summary>
        RoadBlocked = 11,
        /// <summary>
        /// navmesh pathfinding
        /// </summary>
        Map = 20,
        /// <summary>
        /// a* pathfinding on the map(bad performance on big maps)
        /// </summary>
        MapGrid = 21,
        /// <summary>
        /// no pathfinding, just go straight to target
        /// </summary>
        None = 100
    }
}