namespace CityBuilderCore
{
    /// <summary>
    /// convenient class combining a component and a path to get there<br/>
    /// for example when a delivery walker is looking for a storage the receiverpathfinder decides on the receiver and returns a BuildingComponentPath<IItemReceiver>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class BuildingComponentPath<T> where T : IBuildingComponent
    {
        public BuildingComponentReference<T> Component { get; private set; }
        public WalkingPath Path { get; private set; }

        public BuildingComponentPath(BuildingComponentReference<T> component, WalkingPath path)
        {
            Component = component;
            Path = path;
        }
    }
}