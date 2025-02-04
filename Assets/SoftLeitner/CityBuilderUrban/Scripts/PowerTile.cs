using CityBuilderCore;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderUrban
{
    /// <summary>
    /// tile for the power lines in the urban demo<br/>
    /// shows different sprites for 0 / \ X
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/urban">https://citybuilder.softleitner.com/manual/urban</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_urban_1_1_power_tile.html")]
    public class PowerTile : ConnectedTileBase
    {
        [Tooltip("the connection this tile will connect to")]
        public Connection Connection;
        [Tooltip("none | SW | NW | both")]
        public Sprite[] Sprites = new Sprite[4];

        public override Sprite GetPreviewSprite() => Sprites.LastOrDefault();

        protected override Quaternion getRotation(byte mask) => Quaternion.identity;

        protected override Vector3 getScale(byte mask) => Vector3.one;

        protected override Sprite getSprite(byte mask) => Sprites.ElementAtOrDefault(getSpriteIndex(mask));
        
        private int getSpriteIndex(byte mask)
        {
            bool hasNorth = (mask & 1) == 1;
            bool hasSouth = (mask & 8) == 8;

            if (hasSouth && hasNorth)
                return 3;
            else if (hasNorth)
                return 2;
            else if (hasSouth)
                return 1;
            else
                return 0;
        }

        protected override bool hasConnection(ITilemap tilemap, Vector3Int point)
        {
            if (base.hasConnection(tilemap, point))
                return true;

            if (Application.isPlaying)
                return Dependencies.Get<IConnectionManager>().HasPoint(Connection, (Vector2Int)point);
            else
                return false;
        }
    }
}