using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that creates an entry-exit road switch<br/>
    /// road switching can enable walkers to temporarily use foreign road networks<br/>
    /// entry exit switches are special in that a walker can only enter/exit the switch from one point<br/> 
    /// this can be useful for over/underpasses, for an example check out the tunnel scene in the urban demo
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_road_switcher_component.html")]
    public class RoadSwitcherComponent : BuildingComponent
    {
        public override string Key => "ROS";

        [Tooltip("point in the entry road network that the walker has to come from")]
        public Vector2Int EntryPoint;
        [Tooltip("the point at which the switch happens")]
        public Vector2Int SwitchPoint;
        [Tooltip("point in the exit road network that the walker has to go to")]
        public Vector2Int ExitPoint;

        [Tooltip("road network to switch from")]
        public Road EntryRoad;
        [Tooltip("road network to switch to")]
        public Road ExitRoad;

        private void Start()
        {
            Dependencies.Get<IRoadManager>().RegisterSwitch(Building.RotateBuildingPoint(EntryPoint), Building.RotateBuildingPoint(SwitchPoint), Building.RotateBuildingPoint(ExitPoint), EntryRoad, ExitRoad);
        }
        private void OnDestroy()
        {
            if (!gameObject.scene.isLoaded)
                return;
            if (Dependencies.GetOptional<IGameSaver>()?.IsLoading == true)
                return;

            Dependencies.Get<IRoadManager>().Deregister(new[] { Building.RotateBuildingPoint(SwitchPoint) }, EntryRoad);
            Dependencies.Get<IRoadManager>().Deregister(new[] { Building.RotateBuildingPoint(SwitchPoint) }, ExitRoad);
        }
    }
}
