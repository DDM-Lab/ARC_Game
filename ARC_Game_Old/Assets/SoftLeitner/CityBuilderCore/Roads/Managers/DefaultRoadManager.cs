using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// simple road manager implementation that creates a single road network out of any <see cref="Road"/> added<br/>
    /// this means walkers will be able to use any road, if you need seperate road networks per road use <see cref="MultiRoadManager"/><br/>
    /// roads are visualized on the <see cref="Tilemap"/> on the same gameobject as the manager
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_road_manager.html")]
    [RequireComponent(typeof(Tilemap))]
    public class DefaultRoadManager : RoadManagerBase
    {
        protected override RoadNetwork createNetwork()
        {
            return new TilemapRoadNetwork(PathfindingSettings, DefaultRoad, GetComponent<Tilemap>(), Level.Value);
        }
    }
}