using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// tile that changes based on its neighbors<br/>
    /// similar to examples in unity docs and ruletiles in 2d extras<br/>
    /// https://docs.unity3d.com/ScriptReference/Tilemaps.Tilemap.RefreshTile.html <br/>
    /// https://docs.unity3d.com/2021.1/Documentation/Manual/Tilemap-ScriptableTiles-Example.html <br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connected_tile_base.html")]
    public abstract class ConnectedTileBase : TileBase
    {
        [Tooltip("whether the tile should be colored")]
        public bool IsColored;
        [Tooltip("color of the tile when IsColored is true")]
        public Color Color;
        [Tooltip("tiles, other than itself, that this tile connects to")]
        public TileBase[] ConnectedTiles;

        public override bool StartUp(Vector3Int position, ITilemap tilemap, GameObject go)
        {
            if (go)
            {
                var mask = getMask(position, tilemap);

                var rotation = getRotation(mask);
                var scale = getScale(mask);

                if (tilemap.GetComponent<Tilemap>().orientation == Tilemap.Orientation.XY)
                {
                    go.transform.localRotation = rotation;
                    go.transform.localScale = scale;
                }
                else
                {
                    go.transform.localRotation = Quaternion.Euler(0, -rotation.eulerAngles.z, 0);
                    go.transform.localScale = new Vector3(scale.x, 1, scale.y);
                }

                return true;
            }
            else
            {
                return base.StartUp(position, tilemap, go);
            }
        }

        public override void RefreshTile(Vector3Int location, ITilemap tilemap)
        {
            base.RefreshTile(location, tilemap);

            refreshNeighbours(location, tilemap);
        }

        public override void GetTileData(Vector3Int location, ITilemap tilemap, ref TileData tileData)
        {
            base.GetTileData(location, tilemap, ref tileData);

            var byteMask = getMask(location, tilemap);

            tileData.sprite = getSprite(byteMask);
            tileData.gameObject = getPrefab(byteMask);

            var m = tileData.transform;
            m.SetTRS(Vector3.zero, getRotation(byteMask), getScale(byteMask));
            tileData.transform = m;

            tileData.flags = IsColored ? TileFlags.LockAll : TileFlags.LockTransform;
            tileData.colliderType = Tile.ColliderType.None;
            if (IsColored)
                tileData.color = Color;
        }

        public abstract Sprite GetPreviewSprite();
        protected abstract Sprite getSprite(byte mask);
        protected virtual GameObject getPrefab(byte mask) => null;
        protected abstract Quaternion getRotation(byte mask);
        protected abstract Vector3 getScale(byte mask);

        protected virtual void refreshNeighbours(Vector3Int location, ITilemap tilemap)
        {
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

        protected virtual byte getMask(Vector3Int location, ITilemap tilemap)
        {
            int mask = hasConnection(tilemap, location + new Vector3Int(0, 1, 0)) ? 1 : 0;
            mask += hasConnection(tilemap, location + new Vector3Int(1, 0, 0)) ? 2 : 0;
            mask += hasConnection(tilemap, location + new Vector3Int(0, -1, 0)) ? 4 : 0;
            mask += hasConnection(tilemap, location + new Vector3Int(-1, 0, 0)) ? 8 : 0;
            return (byte)mask;
        }

        protected virtual bool hasConnection(ITilemap tilemap, Vector3Int position)
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
