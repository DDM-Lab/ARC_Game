namespace CityBuilderCore
{
    /// <summary>
    /// how new structures in collections are rotated when a new point is added
    /// </summary>
    public enum StructureRotationMode
    {
        /// <summary>
        /// no rotation
        /// </summary>
        None,   
        /// <summary>
        /// random rotation in 90 degree increments
        /// </summary>
        Stepped,
        /// <summary>
        /// random rotation of full 360 degrees
        /// </summary>
        Full
    }
}
