namespace CityBuilderCore
{
    /// <summary>
    /// base class for walkers that roam and perform actions when passing a certain type of building component<br/>
    /// override <see cref="onComponentEntered(T)"/> and <see cref="onComponentRemaining(T)"/> to react to components in the walkers area
    /// </summary>
    public class BuildingComponentWalker<T> : BuildingWalker
    where T : class, IBuildingComponent
    {
        protected override void onEntered(IBuilding building)
        {
            base.onEntered(building);

            foreach (var component in building.GetBuildingComponents<T>())
            {
                onComponentEntered(component);
            }
        }
        protected override void onRemaining(IBuilding building)
        {
            base.onRemaining(building);

            foreach (var component in building.GetBuildingComponents<T>())
            {
                onComponentRemaining(component);
            }
        }

        /// <summary>
        /// called when the building component first enters the walkers area
        /// </summary>
        /// <param name="buildingComponent">the building component inside the walkers area</param>
        protected virtual void onComponentEntered(T buildingComponent)
        {

        }
        /// <summary>
        /// called on every frame the walkers area contains the building component
        /// </summary>
        /// <param name="buildingComponent">the building component inside the walkers area</param>
        protected virtual void onComponentRemaining(T buildingComponent)
        {

        }
    }
}