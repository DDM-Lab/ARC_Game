using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// defines a kind of road that can be placed on the map with a <see cref="RoadBuilder"/> and an <see cref="IRoadManager"/><br/>
    /// in THREE that is just a single road that evolves to a fancier version as defined in <see cref="RoadStage"/><br/>
    /// the urban demo has multiple roads with seperate road networks(<see cref="MultiRoadManager"/>)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_road.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(Road))]
    public class Road : KeyedObject
    {
        [Tooltip("name of the road that may be used in UI")]
        public string Name;
        [Tooltip("road can change appearance and key according to layer values(prettier roads in more desirable areas for example) otherwise just define one stage with no requirements")]
        public RoadStage[] Stages;
        [Tooltip("Items to be subtracted from GlobalStorage for building")]
        public ItemQuantity[] Cost;
        [Tooltip("determines which structures can reside in the same points as this road(when using multi road manager)")]
        public StructureLevelMask Level;
        [Tooltip("whether parts of the road can be destroyed, disable for roads that are part of the map")]
        public bool IsDestructible = true;
        [Tooltip("whether the road can be moved by the MoveTool")]
        public bool IsMovable = true;

        /// <summary>
        /// checks which stage of the road fulfills all layer requirements at the given point
        /// </summary>
        /// <param name="point">the point on the map to check for the requirements</param>
        /// <returns></returns>
        public RoadStage GetStage(Vector2Int point, ILayerManager layerManager = null)
        {
            for (int i = Stages.Length - 1; i >= 0; i--)
            {
                var stage = Stages[i];

                if (stage.LayerRequirements.All(r => r.IsFulfilled(point, layerManager)))
                    return stage;
            }

            return null;
        }
        public TileBase GetTile(Vector2Int point, ILayerManager layerManager = null) => GetStage(point, layerManager)?.Tile;
        public int GetIndex(Vector2Int point, ILayerManager layerManager = null) => GetStage(point, layerManager)?.Index ?? -1;
    }
}