using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// tiles that does not change itself but refreshes connected neighbors
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connectable_tile.html")]
    public class ConnectableTile : TileBase
    {
        [Tooltip("sprite used for the tile")]
        public Sprite Sprite;
        [Tooltip("whether the tile should be colored")]
        public bool IsColored;
        [Tooltip("color of the tile when IsColored is true")]
        public Color Color;
        [Tooltip("tiles that will be refreshed when next to this tile")]
        public TileBase[] ConnectedTiles;

        public override void RefreshTile(Vector3Int location, ITilemap tilemap)
        {
            base.RefreshTile(location, tilemap);

            for (int yd = -1; yd <= 1; yd++)
            {
                for (int xd = -1; xd <= 1; xd++)
                {
                    Vector3Int position = new Vector3Int(location.x + xd, location.y + yd, location.z);
                    if (hasConnection(tilemap, position))
                        tilemap.RefreshTile(position);
                }
            }
        }

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            base.GetTileData(position, tilemap, ref tileData);

            tileData.sprite = Sprite;
            if (IsColored)
                tileData.color = Color;
        }

        protected bool hasConnection(ITilemap tilemap, Vector3Int position)
        {
            TileBase tile = tilemap.GetTile(position);

            if (tile == this)
                return true;

            if (tile == null)
                return false;

            if (ConnectedTiles == null)
                return false;

            return ConnectedTiles.Contains(tile);
        }
    }
}
