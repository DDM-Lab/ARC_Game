using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// simple road manager implementation that creates a single road network out of any <see cref="Road"/> added<br/>
    /// roads are visualized on the <see cref="Terrain"/> on the same gameobject as the manager using a <see cref="TerrainRoadNetwork"/><br/>
    /// when using a <see cref="TerrainModifier"/> with <see cref="TerrainModifier.Alphas"/> the roads are saved there and not persisted by the road manager
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_terrain_road_manager.html")]
    [RequireComponent(typeof(Terrain))]
    public class TerrainRoadManager : RoadManagerBase
    {
        [Tooltip("index of the terrain layer that is set when a road is removed")]
        public int GroundIndex;

        private TerrainModifier _terrainModifier;
        private TerrainRoadNetwork _network;
        private UnityAction _terrainLoadedAction;

        protected override void Awake()
        {
            base.Awake();

            _terrainModifier = GetComponent<TerrainModifier>();
            if (_terrainModifier && !_terrainModifier.Alphas)
                _terrainModifier = null;
        }

        private void OnEnable()
        {
            if (_terrainModifier)
            {
                if (_terrainLoadedAction == null)
                    _terrainLoadedAction = new UnityAction(terrainLoaded);

                _terrainModifier.Loaded.AddListener(_terrainLoadedAction);
            }
        }

        private void OnDisable()
        {
            if (_terrainModifier)
            {
                _terrainModifier.Loaded.RemoveListener(_terrainLoadedAction);
            }
        }

        protected override RoadNetwork createNetwork()
        {
            _network = new TerrainRoadNetwork(PathfindingSettings, DefaultRoad, GetComponent<Terrain>(), !_terrainModifier, GroundIndex, Level.Value);
            return _network;
        }

        private void terrainLoaded()
        {
            _network.Reload();
        }
    }
}
