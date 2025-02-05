using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// utility class that can be used to randomly place some objects<br/>
    /// used in Three for bushes and pebbles
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_object_generator.html")]
    public class ObjectGenerator : MonoBehaviour
    {
#if UNITY_EDITOR
        [System.Serializable]
        public class TerrainHeightRequirement
        {
            public Terrain Terrain;
            public float MinHeight;
            public float MaxHeight;

            public bool Check(Vector3 position)
            {
                var height = Terrain.SampleHeight(position);
                return height >= MinHeight & height <= MaxHeight;
            }
        }

        [System.Serializable]
        public class TilemapTile
        {
            public Tilemap Tilemap;
            public Tile[] Tiles;

            public bool Check(Vector2Int point)
            {
                if (Tiles.Length > 0)
                {
                    return Tiles.Contains(Tilemap.GetTile((Vector3Int)point));

                }
                else
                {
                    return Tilemap.HasTile((Vector3Int)point);
                }
            }
        }

        [System.Serializable]
        public class GeneratorObject
        {
            public float Threshold;
            public Vector3 BaseScale = Vector3.one;
            public GameObject Prefab;
        }

        public enum GeneratorMethod { Rand, Perlin }

        [Header("Conbstraints")]
        public TerrainHeightRequirement[] TerrainHeightRequirements;
        public TilemapTile[] TileRequirements;
        public TilemapTile[] TileBlockers;
        public StructureCollection[] StructureBlockers;
        public StructureCollectionFloat[] StructureFloatBlockers;
        public StructureDecorators[] DecoratorBlockers;
        [Header("Method")]
        public GeneratorMethod Method;
        public Vector2 NoiseScale;
        public Vector2 NoiseOffset;
        public int RandomSeed;
        [Header("Output")]
        public GeneratorObject[] Objects;
        [Tooltip("instances are positioned in the cells corner instead of its center, needed when generating for StructureCollection")]
        public bool Corner;
        public bool Rotate;
        public bool RotateStepped;
        public bool Scale;
        public float ScaleMinimum = 0.8f;
        public float ScaleMaximum = 1.2f;
        [Tooltip("how far objects can randomly be offset from the cell center, should only be used with structures like StructureCollectionFloat that save the world position")]
        [Range(0f, 0.95f)]
        public float OffsetMaximum;

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(transform.GetChild(i).gameObject);
            }
        }

        public void Generate(bool clear = true)
        {
            if (clear)
                Clear();

            Random.InitState(RandomSeed);

            var map = this.FindObjects<MonoBehaviour>().OfType<IMap>().FirstOrDefault();
            var gridPositions = this.FindObjects<MonoBehaviour>().OfType<IGridPositions>().FirstOrDefault();
            var gridRotations = this.FindObjects<MonoBehaviour>().OfType<IGridRotations>().FirstOrDefault();
            var gridHeights = this.FindObjects<MonoBehaviour>().OfType<IGridHeights>().FirstOrDefault();

            if (map == null || gridPositions == null || gridRotations == null)
                return;

            var structureBlocked = StructureBlockers.SelectMany(b => b.GetChildPoints(gridPositions)).ToList();
            var structureFloatBlocked = StructureFloatBlockers.SelectMany(b => b.GetChildPoints(gridPositions)).ToList();
            var decoratorBlocked = DecoratorBlockers.SelectMany(b => b.GetChildPoints(gridPositions)).ToList();

            List<Vector2Int> points = new List<Vector2Int>();
            for (int x = 0; x < map.Size.x; x++)
            {
                for (int y = 0; y < map.Size.y; y++)
                {
                    var point = new Vector2Int(x, y);
                    if (TerrainHeightRequirements != null && TerrainHeightRequirements.Any(t => !t.Check(gridPositions.GetWorldCenterPosition(point))))
                        continue;

                    if (!TileRequirements.All(t => t.Check(point)))
                        continue;
                    if (TileBlockers.Any(t => t.Check(point)))
                        continue;

                    if (structureBlocked.Contains(point))
                        continue;
                    if (structureFloatBlocked.Contains(point))
                        continue;
                    if (decoratorBlocked.Contains(point))
                        continue;

                    points.Add(point);
                }
            }

            Random.InitState(RandomSeed);

            foreach (var point in points)
            {
                float value;
                switch (Method)
                {
                    default:
                    case GeneratorMethod.Rand:
                        value = Random.Range(0f, 1f);
                        break;
                    case GeneratorMethod.Perlin:
                        value = Mathf.PerlinNoise(point.x * NoiseScale.x + NoiseOffset.x, point.y * NoiseScale.y + NoiseOffset.y);
                        break;
                }
                GeneratorObject generatorObject = null;
                foreach (var o in Objects)
                {
                    if (o.Threshold > value)
                        break;
                    generatorObject = o;
                }

                if (generatorObject != null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(generatorObject.Prefab, transform);

                    instance.transform.position = Corner ? gridPositions.GetWorldPosition(point) : gridPositions.GetWorldCenterPosition(point);

                    if (Rotate)
                    {
                        float rotation;

                        if (RotateStepped)
                            rotation = Random.Range(0, 3) * 90;
                        else
                            rotation = Random.Range(0f, 360f);

                        gridRotations.SetRotation(instance.transform, rotation);
                    }

                    if (Scale)
                    {
                        instance.transform.localScale = generatorObject.BaseScale * Random.Range(ScaleMinimum, ScaleMaximum);
                    }

                    if (OffsetMaximum != 0f)
                    {
                        var offset = new Vector3(
                            map.CellOffset.x * UnityEngine.Random.Range(-OffsetMaximum / 2f, OffsetMaximum / 2f),
                            map.CellOffset.y * UnityEngine.Random.Range(-OffsetMaximum / 2f, OffsetMaximum / 2f),
                            map.CellOffset.z * UnityEngine.Random.Range(-OffsetMaximum / 2f, OffsetMaximum / 2f));

                        if (map.IsXY)
                            offset.z = 0f;
                        else
                            offset.y = 0f;

                        instance.transform.position += offset;
                    }

                    if (gridHeights != null)
                    {
                        var height = gridHeights.GetHeight(Corner ? gridPositions.GetCenterFromPosition(instance.transform.position) : instance.transform.position);
                        if (height != 0)
                        {
                            if (map.IsXY)
                                instance.transform.localPosition = new Vector3(instance.transform.localPosition.x, instance.transform.localPosition.y, height);
                            else
                                instance.transform.localPosition = new Vector3(instance.transform.localPosition.x, height, instance.transform.localPosition.z);
                        }
                    }
                }
            }
        }
#endif
    }
}
