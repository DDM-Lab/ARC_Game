namespace CityBuilderCore
{
    /// <summary>
    /// how the points a building might be built on are checked for its requirements<br/>
    /// ie are farms allowed only fully on grass or is one point enough
    /// </summary>
    public enum BuildingRequirementMode
    {
        /// <summary>
        /// a number of building points equal or larger than whats specified in count has to fulfill the requirements
        /// </summary>
        Any,
        /// <summary>
        /// the requirements have to be fulfilled when summed up and divided by the number of points
        /// </summary>
        Average,
        /// <summary>
        /// specify the exact points that have to fulfill the requirements
        /// </summary>
        Specific,
        /// <summary>
        /// all of the buildings points have to fulfill the requirements
        /// </summary>
        All,
        /// <summary>
        /// a number of the specified points equal or larger than whats specified in count has to fulfill the requirements
        /// </summary>
        AnySpecific,
    }
}
