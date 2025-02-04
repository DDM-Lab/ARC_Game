namespace CityBuilderCore
{
    /// <summary>
    /// describes how and if walkers are started off(for example finding a path before spawning vs just spawning and letting the walker find a path itself)
    /// </summary>
    public enum WalkerInitializationMode
    {
        /// <summary>
        /// walker is fully initialized up front<br/>
        /// no spawn if conditions for walking are not met
        /// </summary>
        Instant,
        /// <summary>
        /// walker is initialized using a query<br/>
        /// query is completed a little later allowing load distribution <br/>
        /// no spawn if conditions for walking are not met
        /// </summary>
        Prepared,
        /// <summary>
        /// walker always spawns and figures out pathing while Delay/MaxWait runs<br/>
        /// if no path is found the walker just waits in front of the building and eventually despawns
        /// </summary>
        Delayed,
    }
}
