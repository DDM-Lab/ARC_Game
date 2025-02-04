namespace CityBuilderCore
{
    /// <summary>
    /// which points are used to access the building
    /// </summary>
    public enum BuildingAccessType
    {
        /// <summary>
        /// use any point around the building
        /// </summary>
        Any = 0,
        /// <summary>
        /// use only the specified point
        /// </summary>
        Exclusive = 10,
        /// <summary>
        /// try to use the specified point if possible, otherwise use any
        /// </summary>
        Preferred = 20
    }
}