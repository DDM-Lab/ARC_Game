using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// any kind of value a walker might have(items, ...)
    /// </summary>
    public interface IWalkerValue
    {
        /// <summary>
        /// whether the value even applies to the walker in question
        /// </summary>
        /// <param name="walker">the walker in question</param>
        /// <returns>true when the walker has this value</returns>
        bool HasValue(Walker walker);
        /// <summary>
        /// the maximum value this value may get to<br/>
        /// for example storage capacity for item quantities<br/>
        /// important for bars that use a ratio
        /// </summary>
        /// <param name="walker">the walker in question</param>
        /// <returns>max value the walker might ever return</returns>
        float GetMaximum(Walker walker);
        /// <summary>
        /// checks for the value the walker currently has
        /// </summary>
        /// <param name="walker">the walker of which we want to know the value</param>
        /// <returns>the value of the walker</returns>
        float GetValue(Walker walker);
        /// <summary>
        /// the world position that the value should be visualized at<br/>
        /// especially relevant for global values that are not created as a child of the walker
        /// </summary>
        /// <param name="walker">the walker in question</param>
        /// <returns>where the value should be visualized</returns>
        Vector3 GetPosition(Walker walker);
    }
}