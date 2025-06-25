using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// road manager implementation that can create seperate road networks for different <see cref="Road"/>s<br/>
    /// it also creates a combined road network for walkers which do not specifiy which road they can walk on<br/>
    /// the kind of <see cref="Road"/> a walker can walk on is specified in <see cref="WalkerInfo.PathTag"/><br/>
    /// roads are visualized on the <see cref="Tilemap"/> on the same gameobject as the manager
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_multi_road_manager.html")]
    public class MultiRoadManager : RoadManagerBaseMulti
    {
        /// <summary>
        /// helper that combines a road with the tilemap it is visualized on
        /// </summary>
        [Serializable]
        public class Network
        {
            public Road Road;
            public Tilemap Tilemap;
        }

        [Tooltip("the different road networks in this manager")]
        public Network[] Networks;

        protected override Dictionary<Road, RoadNetwork> createNetworks()
        {
            var networks = new Dictionary<Road, RoadNetwork>();
            Networks.ForEach(r => networks.Add(r.Road, new TilemapRoadNetwork(PathfindingSettings, r.Road, r.Tilemap)));
            return networks;
        }
    }
}