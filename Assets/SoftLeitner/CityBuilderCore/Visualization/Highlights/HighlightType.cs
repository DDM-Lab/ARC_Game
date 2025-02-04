namespace CityBuilderCore
{
    /// <summary>
    /// specifies how a point on the map should be visualized by the <see cref="IHighlightManager"/>
    /// </summary>
    public enum HighlightType
    {
        /// <summary>
        /// probably green
        /// </summary>
        Valid,
        /// <summary>
        /// probably red
        /// </summary>
        Invalid,
        /// <summary>
        /// blue?
        /// </summary>
        Info
    }
}