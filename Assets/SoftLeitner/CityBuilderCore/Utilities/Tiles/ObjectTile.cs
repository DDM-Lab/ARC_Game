using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// tile that instantiates gameobjects
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_object_tile.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Tiles/" + nameof(ObjectTile))]
    public class ObjectTile : TileBase
    {
        [Tooltip("gameobject that will be instantiated on every tile")]
        public GameObject Prefab;

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            base.GetTileData(position, tilemap, ref tileData);

            tileData.gameObject = Prefab;
        }
    }
}
