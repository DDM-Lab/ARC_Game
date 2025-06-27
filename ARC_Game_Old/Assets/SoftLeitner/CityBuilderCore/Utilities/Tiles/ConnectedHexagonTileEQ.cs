using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// tile that can be used for connections in hex grids with equidistant sizes<br/>
    /// check out CityBuilderCore.Tests/Other/HexRoad for examples
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connected_hexagon_tile_e_q.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Tiles/" + nameof(ConnectedHexagonTileEQ))]
    public class ConnectedHexagonTileEQ : ConnectedTileBase
    {
        [Tooltip("0 | 1 | 222 | 333 | 444 | 5 | 6")]
        public Sprite[] Sprites = new Sprite[13];
        [Tooltip("0 | 1 | 222 | 333 | 444 | 5 | 6")]
        public GameObject[] Prefabs = new GameObject[13];

        public override Sprite GetPreviewSprite() => Sprites.LastOrDefault();

        protected override byte getMask(Vector3Int location, ITilemap tilemap)
        {
            int mask = 0;

            if (location.y % 2 == 0)
            {
                mask += hasConnection(tilemap, location + new Vector3Int(0, 1, 0)) ? 1 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(1, 0, 0)) ? 2 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(0, -1, 0)) ? 4 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(-1, -1, 0)) ? 8 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(-1, 0, 0)) ? 16 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(-1, 1, 0)) ? 32 : 0;
            }
            else
            {
                mask += hasConnection(tilemap, location + new Vector3Int(1, 1, 0)) ? 1 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(1, 0, 0)) ? 2 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(1, -1, 0)) ? 4 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(0, -1, 0)) ? 8 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(-1, 0, 0)) ? 16 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(0, 1, 0)) ? 32 : 0;
            }

            return (byte)mask;
        }

        protected override Quaternion getRotation(byte mask) => Quaternion.Euler(0, 0, -getZRotation(mask));

        protected override Vector3 getScale(byte mask)
        {
            switch (mask)
            {
                case 22:
                case 44:
                case 25:
                case 50:
                case 37:
                case 11: return new Vector3(1, -1, 1);
                default: return Vector3.one;
            }
        }

        protected override Sprite getSprite(byte mask) => Sprites.ElementAtOrDefault(getSpriteIndex(mask));
        protected override GameObject getPrefab(byte mask) => Prefabs.ElementAtOrDefault(getSpriteIndex(mask));

        private int getSpriteIndex(byte mask)
        {
            switch (mask)
            {
                //NONE
                case 0: return 0;
                //ONE
                case 2:
                case 16:
                case 1:
                case 4:
                case 8:
                case 32: return 1;
                //TWO
                case 18:
                case 9:
                case 36: return 2;
                case 34:
                case 5:
                case 10:
                case 20:
                case 40:
                case 17: return 3;
                case 33:
                case 12:
                case 3:
                case 6:
                case 24:
                case 48: return 4;
                //THREE
                case 19:
                case 38:
                case 13:
                case 26:
                case 52:
                case 41: return 5;
                case 21:
                case 42: return 6;
                case 35:
                case 7:
                case 14:
                case 28:
                case 56:
                case 49: return 7;
                case 22:
                case 44:
                case 25:
                case 50:
                case 37:
                case 11: return 5;//mirrored
                //FOUR
                case 51:
                case 39:
                case 15:
                case 30:
                case 60:
                case 57: return 8;
                case 23:
                case 46:
                case 29:
                case 58:
                case 53:
                case 43: return 9;
                case 54:
                case 45:
                case 27: return 10;
                //FIVE
                case 31:
                case 62:
                case 59:
                case 55:
                case 47:
                case 61: return 11;
                //SIX
                case 63: return 12;
            }

            return -1;
        }

        private int getZRotation(byte mask)
        {
            switch (mask)
            {
                //NONE
                case 0: return 0;

                //ONE
                case 2: return 0;
                case 4: return 60;
                case 8: return 120;
                case 16: return 180;
                case 32: return 240;
                case 1: return 300;

                //TWO
                case 18: return 0;
                case 36: return 60;
                case 9: return 120;

                case 34: return 0;
                case 5: return 60;
                case 10: return 120;
                case 20: return 180;
                case 40: return 240;
                case 17: return 300;

                case 3: return 0;
                case 6: return 60;
                case 12: return 120;
                case 24: return 180;
                case 48: return 240;
                case 33: return 300;

                //THREE
                case 19: return 0;
                case 38: return 60;
                case 13: return 120;
                case 26: return 180;
                case 52: return 240;
                case 41: return 300;

                case 21: return 0;
                case 42: return 60;

                case 35: return 0;
                case 7: return 60;
                case 14: return 120;
                case 28: return 180;
                case 56: return 240;
                case 49: return 300;

                case 22: return 0;
                case 44: return 60;
                case 25: return 120;
                case 50: return 180;
                case 37: return 240;
                case 11: return 300;//mirrored

                //FOUR
                case 51: return 0;
                case 39: return 60;
                case 15: return 120;
                case 30: return 180;
                case 60: return 240;
                case 57: return 300;

                case 23: return 0;
                case 46: return 60;
                case 29: return 120;
                case 58: return 180;
                case 53: return 240;
                case 43: return 300;

                case 54: return 0;
                case 45: return 60;
                case 27: return 120;

                //FIVE
                case 47: return 0;
                case 31: return 60;
                case 62: return 120;
                case 61: return 180;
                case 59: return 240;
                case 55: return 300;

                //SIX
                case 63: return 0;
            }

            return 0;
        }
    }
}
