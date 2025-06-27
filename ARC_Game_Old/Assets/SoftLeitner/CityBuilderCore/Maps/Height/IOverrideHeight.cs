namespace CityBuilderCore
{
    /// <summary>
    /// interface for entities that can have their height overridden(walkers)<br/>
    /// typically this is set for obstacles that are not known to the map<br/>
    /// for example in THREE to override the height of walkers when they walk over bridges
    /// </summary>
    public interface IOverrideHeight
    {
        /// <summary>
        /// height that the entity is forced to have, null to use the regular height(terrain or road height)
        /// </summary>
        float? HeightOverride { get; set; }
    }
}
