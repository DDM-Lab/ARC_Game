using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// any kind of value a building might have(risks, services, items, ...)
    /// </summary>
    public interface IBuildingValue
    {
        /// <summary>
        /// whether the value even applies to the building in question
        /// </summary>
        /// <param name="building">the building in question</param>
        /// <returns>true when the building has this value</returns>
        bool HasValue(IBuilding building);
        /// <summary>
        /// the maximum value this value may get to<br/>
        /// important for bars that use a ratio
        /// </summary>
        /// <param name="building">the building in question</param>
        /// <returns>max value the building might ever return</returns>
        float GetMaximum(IBuilding building);
        /// <summary>
        /// checks for the value the building currently has
        /// </summary>
        /// <param name="building">the building of which we want to know the value</param>
        /// <returns>the value of the building</returns>
        float GetValue(IBuilding building);
        /// <summary>
        /// the world position that the value should be visualized at<br/>
        /// especially relevant for global values that are not created as a child of the building
        /// </summary>
        /// <param name="building">the building in question</param>
        /// <returns>where the value should be visualized</returns>
        Vector3 GetPosition(IBuilding building);
    }
}