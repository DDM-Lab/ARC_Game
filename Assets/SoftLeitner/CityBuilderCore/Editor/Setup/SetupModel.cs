using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore.Editor
{
    [Serializable]
    public class SetupModel
    {
        public enum CityDisplayMode { Mesh, Sprite }
        public enum MapDisplayMode { Mesh, Sprite, Terrain }
        public enum MapLayoutMode { Rectangle, Hexagon, HexagonFlatTop, Isometric }
        public enum MapAxisMode { XY, XZ }

        public string Directory = "MyCityBuilder";
        public CityDisplayMode CityDisplay = CityDisplayMode.Mesh;
        public MapDisplayMode MapDisplay = MapDisplayMode.Mesh;
        public MapLayoutMode MapLayout = MapLayoutMode.Rectangle;
        public MapAxisMode MapAxis = MapAxisMode.XZ;
        public Vector2Int MapSize = new Vector2Int(32, 32);
        public float Scale = 1f;

        public bool IsHexagonal => MapLayout == MapLayoutMode.Hexagon || MapLayout == MapLayoutMode.HexagonFlatTop;

        public GridLayout.CellLayout CellLayout
        {
            get
            {
                switch (MapLayout)
                {
                    case MapLayoutMode.Hexagon:
                    case MapLayoutMode.HexagonFlatTop:
                        return GridLayout.CellLayout.Hexagon;
                    case MapLayoutMode.Isometric:
                        return GridLayout.CellLayout.Isometric;
                    default:
                        return GridLayout.CellLayout.Rectangle;
                }
            }
        }
        public GridLayout.CellSwizzle CellSwizzle
        {
            get
            {
                if (MapLayout == MapLayoutMode.HexagonFlatTop)
                {
                    return MapAxis == MapAxisMode.XY ? GridLayout.CellSwizzle.YXZ : GridLayout.CellSwizzle.YZX;
                }
                else
                {
                    return MapAxis == MapAxisMode.XY ? GridLayout.CellSwizzle.XYZ : GridLayout.CellSwizzle.XZY;
                }
            }
        }
        public Vector3 CellSize => MapLayout == MapLayoutMode.Isometric ? new Vector3(Scale, Scale / 2f, Scale) : Vector3.one * Scale;
        public Tilemap.Orientation TilemapOrientation
        {
            get
            {
                if (MapLayout == MapLayoutMode.HexagonFlatTop)
                {
                    return MapAxis == MapAxisMode.XY ? Tilemap.Orientation.YX : Tilemap.Orientation.ZX;
                }
                else
                {
                    return MapAxis == MapAxisMode.XY ? Tilemap.Orientation.XY : Tilemap.Orientation.XZ;
                }
            }
        }
        public Vector3 TilemapAnchor => IsHexagonal ? Vector3.zero : new Vector3(0.5f, 0.5f, 0.0f);

        public string AssetDirectory => string.IsNullOrWhiteSpace(Directory) ? "Assets" : "Assets/" + Directory;
        public string GetAssetPath(string asset, string folder = null)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return AssetDirectory + "/" + asset;
            else
                return AssetDirectory + "/" + folder + "/" + asset;
        }
    }
}
