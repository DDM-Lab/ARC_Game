using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for views that visualize a <see cref="IBuildingValue"/> with a bar
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class ViewBuildingBar<T> : ViewBuildingBarBase where T : IBuildingValue
    {
        [Tooltip("the building value displayed by this view")]
        public T Value;

        public override IBuildingValue BuildingValue => Value;
    }
}