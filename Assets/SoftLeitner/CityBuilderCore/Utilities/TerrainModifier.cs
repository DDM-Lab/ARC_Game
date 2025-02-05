using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// used to modify and persist terrain data
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_terrain_modifier.html")]
    [RequireComponent(typeof(Terrain))]
    public class TerrainModifier : ExtraDataBehaviour
    {
        [Header("Persistence")]
        [Tooltip("whether height data should be persisted, it may be changed by random map generation or some custom terrain changes")]
        public bool Heights = true;
        [Tooltip("whether alphas should be persisted, it may be changed by random map generation, terrain roads can also be changed by this")]
        public bool Alphas = true;
        [Tooltip("whether trees should be persisted, for example when trees are used in structures")]
        public bool Trees = true;
        [Tooltip("whether details should be persisted, for example when details like grass are removed when built on")]
        public bool Details = true;

        [Tooltip("fired after the terrain data has been loaded from persistence, can be used to update dependent systems like navmesh")]
        public UnityEvent Loaded;

        private LazyDependency<IGridPositions> _gridPositions = new LazyDependency<IGridPositions>();
        private Terrain _terrain;

        private List<TreeInstance> _trees;
        private bool _treesChanged;

        private void Awake()
        {
            _terrain = GetComponent<Terrain>();
            _terrain.terrainData = Instantiate(_terrain.terrainData);

            var collider = GetComponent<TerrainCollider>();
            if (collider)
                collider.terrainData = _terrain.terrainData;

            Initialize();
        }

        private void LateUpdate()
        {
            if (_treesChanged)
                _terrain.terrainData.SetTreeInstances(_trees.ToArray(), false);
        }

        public void Initialize()
        {
            _trees = _terrain.terrainData.treeInstances.ToList();
        }

        public IEnumerable<Vector2Int> GetTreePoints(int index = -1)
        {
            foreach (var tree in _trees)
            {
                if (index >= 0 && tree.prototypeIndex != index)
                    continue;
                yield return getTreePoint(tree.position);
            }
        }
        public IEnumerable<Vector2Int> GetTreePoints(IEnumerable<int> indices)
        {
            foreach (var tree in _trees)
            {
                if (!indices.Contains(tree.prototypeIndex))
                    continue;
                yield return getTreePoint(tree.position);
            }
        }

        public void RemoveTrees(Vector2Int point, int index = -1)
        {
            for (int i = _trees.Count - 1; i >= 0; i--)
            {
                var tree = _trees[i];

                if (index >= 0 && tree.prototypeIndex != index)
                    continue;
                if (point != getTreePoint(tree.position))
                    continue;

                _trees.RemoveAt(i);
            }

            _treesChanged = true;
        }
        public void RemoveTrees(Vector2Int point, IEnumerable<int> indices)
        {
            for (int i = _trees.Count - 1; i >= 0; i--)
            {
                var tree = _trees[i];

                if (!indices.Contains(tree.prototypeIndex))
                    continue;
                if (point != getTreePoint(tree.position))
                    continue;

                _trees.RemoveAt(i);
            }

            _treesChanged = true;
        }

        public void AddTree(Vector2Int point, int index = 0, float height = 1f, float width = 1f, float color = 1f)
        {
            var position = _gridPositions.Value.GetWorldCenterPosition(point);
            position.y = _terrain.SampleHeight(position);
            position = new Vector3(position.x / _terrain.terrainData.size.x, position.y / _terrain.terrainData.size.y, position.z / _terrain.terrainData.size.z);

            if (index < 0)
                index = UnityEngine.Random.Range(0, _terrain.terrainData.treePrototypes.Length);

            _trees.Add(new TreeInstance()
            {
                prototypeIndex = index,
                position = position,
                heightScale = height,
                widthScale = width,
                color = new Color(color, color, color),
                lightmapColor = Color.white
            });

            _treesChanged = true;
        }
        public void AddTree(Vector2Int point, TreeInstance template, int index = 0)
        {
            if (index < 0)
                index = UnityEngine.Random.Range(0, _terrain.terrainData.treePrototypes.Length);

            _trees.Add(new TreeInstance()
            {
                prototypeIndex = index,
                position = template.position,
                rotation = template.rotation,
                heightScale = template.heightScale,
                widthScale = template.widthScale,
                color = template.color,
                lightmapColor = Color.white
            });

            _treesChanged = true;
        }

        public TreeInstance GetTree(Vector2Int point, int index)
        {
            for (int i = _trees.Count - 1; i >= 0; i--)
            {
                var tree = _trees[i];

                if (index >= 0 && tree.prototypeIndex != index)
                    continue;
                if (point != getTreePoint(tree.position))
                    continue;

                return tree;
            }
            return default;
        }
        public TreeInstance GetTree(Vector2Int point, IEnumerable<int> indices)
        {
            for (int i = _trees.Count - 1; i >= 0; i--)
            {
                var tree = _trees[i];

                if (!indices.Contains(tree.prototypeIndex))
                    continue;
                if (point != getTreePoint(tree.position))
                    continue;

                return tree;
            }
            return default;
        }

        public void RemoveDetails(Vector2Int point, int index = -1)
        {
            if (index < 0)
            {
                for (int i = 0; i < _terrain.terrainData.detailPrototypes.Length; i++)
                {
                    removeDetails(point, i);
                }
            }
            else
            {
                removeDetails(point, index);
            }
        }
        private void removeDetails(Vector2Int point, int index)
        {
            var position = _gridPositions.Value.GetWorldPosition(point);
            var size = _terrain.terrainData.size;
            var factor = new Vector3(position.x / size.x, position.y / size.y, position.z / size.z);

            var detailPoint = new Vector2(_terrain.terrainData.detailHeight * factor.x, _terrain.terrainData.detailWidth * factor.z);

            var map = Dependencies.Get<IMap>();
            var detailSize = new Vector2Int(_terrain.terrainData.detailWidth / map.Size.x, _terrain.terrainData.detailHeight / map.Size.y);

            _terrain.terrainData.SetDetailLayer(Mathf.FloorToInt(detailPoint.x), Mathf.FloorToInt(detailPoint.y), index, new int[detailSize.x, detailSize.y]);
        }

        private Vector2Int getTreePoint(Vector3 position)
        {
            return _gridPositions.Value.GetGridPoint(Vector3.Scale(position, _terrain.terrainData.size));
        }

        #region Saving
        [Serializable]
        public class TerrainModifierData
        {
            public string Heights;
            public string Alphas;
            public List<TerrainModifierTree> Trees;
            public List<string> Details;
        }
        [Serializable]
        public struct TerrainModifierTree
        {
            public Vector3 position;
            public float widthScale;
            public float heightScale;
            public float rotation;
            public Color32 color;
            public Color32 lightmapColor;
            public int prototypeIndex;

            public TreeInstance ToInstance()
            {
                return new TreeInstance()
                {
                    position = position,
                    widthScale = widthScale,
                    heightScale = heightScale,
                    rotation = rotation,
                    color = color,
                    lightmapColor = lightmapColor,
                    prototypeIndex = prototypeIndex
                };
            }
            public static TerrainModifierTree FromInstance(TreeInstance tree)
            {
                return new TerrainModifierTree()
                {
                    position = tree.position,
                    widthScale = tree.widthScale,
                    heightScale = tree.heightScale,
                    rotation = tree.rotation,
                    color = tree.color,
                    lightmapColor = tree.lightmapColor,
                    prototypeIndex = tree.prototypeIndex
                };
            }
        }

        public override string SaveData()
        {
            //todo compression for byte arrays and efficient format for trees

            var tData = _terrain.terrainData;
            var data = new TerrainModifierData();

            if (Heights)
            {
                data.Heights = Convert.ToBase64String(tData.GetHeights(0, 0, tData.heightmapResolution, tData.heightmapResolution).ToBytes());
            }

            if (Alphas)
            {
                data.Alphas = Convert.ToBase64String(tData.GetAlphamaps(0, 0, tData.alphamapWidth, tData.alphamapHeight).ToBytes());
            }

            if (Trees)
            {
                data.Trees = _trees.Select(TerrainModifierTree.FromInstance).ToList();
            }

            if (Details)
            {
                data.Details = new List<string>();
                for (int i = 0; i < tData.detailPrototypes.Length; i++)
                {
                    data.Details.Add(Convert.ToBase64String(tData.GetDetailLayer(0, 0, tData.detailWidth, tData.detailWidth, i).ToBytes()));
                }
            }

            return JsonUtility.ToJson(data);
        }

        public override void LoadData(string json)
        {
            var tData = _terrain.terrainData;
            var data = JsonUtility.FromJson<TerrainModifierData>(json);

            if (Heights && !string.IsNullOrWhiteSpace(data.Heights))
            {
                var heights = new float[tData.heightmapResolution, tData.heightmapResolution];

                heights.FromBytes(Convert.FromBase64String(data.Heights));

                tData.SetHeights(0, 0, heights);
            }

            if (Alphas && data.Alphas != null)
            {
                var alphas = new float[tData.alphamapWidth, tData.alphamapHeight, tData.alphamapLayers];

                alphas.FromBytes(Convert.FromBase64String(data.Alphas));

                tData.SetAlphamaps(0, 0, alphas);
            }

            if (Trees && data.Trees != null)
            {
                _trees = data.Trees.Select(t => t.ToInstance()).ToList();
                _terrain.terrainData.SetTreeInstances(_trees.ToArray(), false);
            }

            if (Details && data.Details != null)
            {
                for (int i = 0; i < data.Details.Count; i++)
                {
                    var layer = new int[tData.detailWidth, tData.detailHeight];

                    layer.FromBytes(Convert.FromBase64String(data.Details[i]));

                    _terrain.terrainData.SetDetailLayer(0, 0, i, layer);
                }
            }

            Loaded?.Invoke();
        }
        #endregion
    }
}