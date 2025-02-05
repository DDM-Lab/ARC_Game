using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// connecting tile for isometric grids, used for roads and rails in the urban demo
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connected_isometric_tile.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Tiles/" + nameof(ConnectedIsometricTile))]
    public class ConnectedIsometricTile : ConnectedTileBase
    {
        [Tooltip("none | SW | SW-NE | SW-SE | SW-NW | SW-NW-SE | all")]
        public Sprite[] Sprites = new Sprite[7];
        [Tooltip("none | SW | SW-NE | SW-SE | SW-NW | SW-NW-SE | all")]
        public GameObject[] Prefabs = new GameObject[7];

        public override Sprite GetPreviewSprite() => Sprites.LastOrDefault();

        protected override Quaternion getRotation(byte mask) => Quaternion.identity;

        protected override Vector3 getScale(byte mask)
        {
            switch (mask)
            {
                //NONE
                case 0: return Vector3.one;
                //SINGLE
                case 1: return new Vector3(1, -1, 1);
                case 2: return new Vector3(-1, -1, 1);
                case 4: return new Vector3(-1, 1, 1);
                case 8: return new Vector3(1, 1, 1);
                //DOUBLE I
                case 5: return new Vector3(-1, 1, 1);
                case 10: return new Vector3(1, 1, 1);
                //DOUBLE L SOUTH
                case 3: return new Vector3(1, -1, 1);
                case 12: return new Vector3(1, 1, 1);
                //DOUBLE L EAST
                case 6: return new Vector3(-1, 1, 1);
                case 9: return new Vector3(1, 1, 1);
                //TRIPLE
                case 7: return new Vector3(-1, -1, 1);
                case 11: return new Vector3(1, -1, 1);
                case 13: return new Vector3(1, 1, 1);
                case 14: return new Vector3(-1, 1, 1);
                //FULL
                case 15: return Vector3.one;
            }

            return Vector3.one;
        }

        protected override Sprite getSprite(byte mask) => Sprites.ElementAtOrDefault(getSpriteIndex(mask));
        protected override GameObject getPrefab(byte mask) => Prefabs.ElementAtOrDefault(getSpriteIndex(mask));

        private int getSpriteIndex(byte mask)
        {
            switch (mask)
            {
                //NONE
                case 0: return 0;
                //SINGLE
                case 1:
                case 2:
                case 4:
                case 8: return 1;
                //DOUBLE I
                case 5:
                case 10: return 2;
                //DOUBLE L SOUTH
                case 3:
                case 12: return 3;
                //DOUBLE L EAST
                case 6:
                case 9: return 4;
                //TRIPLE
                case 7:
                case 11:
                case 13:
                case 14: return 5;
                //FULL
                case 15: return 6;
            }

            return -1;
        }
    }
}
