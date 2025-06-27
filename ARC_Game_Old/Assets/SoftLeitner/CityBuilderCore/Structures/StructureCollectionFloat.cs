using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// structure made up of a collection of gameobjects each occupying exactly one point on the map<br/>
    /// saves full position instead of just points like <see cref="StructureCollection"/><br/>
    /// if the members of the collection are <see cref="ISaveData"/> that data will also be stored
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/structures">https://citybuilder.softleitner.com/manual/structures</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_structure_collection_float.html")]
    public class StructureCollectionFloat : KeyedBehaviour, IStructure
    {
        [Serializable]
        public class Variant
        {
            [Tooltip("prefab instantiated by this variant")]
            public GameObject Prefab;
            [Tooltip("how far newly created objects can randomly be offset from the cell center")]
            [Range(0f, 0.95f)]
            public float OffsetMaximum;
            [Tooltip("how newly added objects are rotated(no rotation | rand 90° steps | rand full float)")]
            public StructureRotationMode RotationMode;
            [Tooltip("lower bound for random scale of new objects")]
            public float ScaleMinimum = 1f;
            [Tooltip("upper bound for random scale of new objects")]
            public float ScaleMaximum = 1f;

            public GameObject Instantiate(Transform parent, Vector2Int point, IMap map, IGridPositions gridPositions, IGridRotations gridRotations)
            {
                var instance = UnityEngine.Object.Instantiate(Prefab, parent);
                Adjust(instance, point, map, gridPositions, gridRotations);
                return instance;
            }
            public GameObject Instantiate(Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
            {
                var instance = UnityEngine.Object.Instantiate(Prefab, parent);
                instance.transform.localPosition = localPosition;
                instance.transform.localRotation = localRotation;
                instance.transform.localScale = localScale;
                return instance;
            }

            public void Adjust(GameObject instance, Vector2Int point, IMap map, IGridPositions gridPositions, IGridRotations gridRotations)
            {
                instance.transform.position = gridPositions.GetWorldCenterPosition(point);

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

                instance.transform.localScale = Prefab.transform.localScale;

                if (ScaleMinimum != 0f && ScaleMaximum != 0f && !(ScaleMinimum == 1f && ScaleMaximum == 1f))
                    instance.transform.localScale *= UnityEngine.Random.Range(ScaleMinimum, ScaleMaximum);

                switch (RotationMode)
                {
                    case StructureRotationMode.Stepped:
                        gridRotations.SetRotation(instance.transform, UnityEngine.Random.Range(0, 3) * 90);
                        break;
                    case StructureRotationMode.Full:
                        gridRotations.SetRotation(instance.transform, UnityEngine.Random.Range(0f, 360f));
                        break;
                }
            }
        }

        [Tooltip("name of the structure in the UI")]
        public string Name;
        [Tooltip("whether the structure can be removed by the player")]
        public bool IsDestructible = true;
        [Tooltip("whether the structure can be moved by the MoveTool")]
        public bool IsMovable = true;
        [Tooltip("whether the structure is automatically removed when something is built on top of it")]
        public bool IsDecorator = false;
        [Tooltip("whether walkers can pass the points of this structure")]
        public bool IsWalkable = false;
        [Tooltip("determines which other structures can reside in the same points")]
        public StructureLevelMask Level;
        [Tooltip("all the prefabs that can be used for new points or when loading, gameobject names of already placed objects have to start with the prefab name")]
        public Variant[] Variants;

        bool IStructure.IsDestructible => IsDestructible;
        bool IStructure.IsMovable => IsMovable;
        bool IStructure.IsDecorator => IsDecorator;
        bool IStructure.IsWalkable => IsWalkable;
        int IStructure.Level => Level.Value;

        public StructureReference StructureReference { get; set; }

        public Transform Root => transform;

        public event Action<PointsChanged<IStructure>> PointsChanged;

        private Dictionary<Vector2Int, GameObject> _objects = new Dictionary<Vector2Int, GameObject>();
        private IGridPositions _gridPositions;
        private IGridRotations _gridRotations;
        private IGridHeights _gridHeights;

        private void Start()
        {
            _gridPositions = Dependencies.Get<IGridPositions>();
            _gridRotations = Dependencies.Get<IGridRotations>();
            _gridHeights = Dependencies.GetOptional<IGridHeights>();

            foreach (Transform child in transform)
            {
                _objects.Add(_gridPositions.GetGridPoint(child.position), child.gameObject);
            }

            StructureReference = new StructureReference(this);
            Dependencies.Get<IStructureManager>().RegisterStructure(this);

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, Enumerable.Empty<Vector2Int>(), GetPoints()));
        }

        public IEnumerable<Vector2Int> GetChildPoints(IGridPositions positions)
        {
            foreach (Transform child in transform)
            {
                yield return positions.GetGridPoint(child.position);
            }
        }

        private void OnDestroy()
        {
            if (gameObject.scene.isLoaded)
                Dependencies.Get<IStructureManager>().DeregisterStructure(this);
        }

        public IEnumerable<Vector2Int> GetPoints() => _objects.Keys;

        public bool HasPoint(Vector2Int point) => _objects.ContainsKey(point);

        public void Add(Vector2Int point) => Add(new Vector2Int[] { point });
        public void Add(IEnumerable<Vector2Int> points)
        {
            var map = Dependencies.Get<IMap>();

            foreach (var point in points)
            {
                if (_objects.ContainsKey(point))
                    continue;

                var instance = Variants.Random().Instantiate(transform, point, map, _gridPositions, _gridRotations);

                _gridHeights?.ApplyHeight(instance.transform);
                _objects.Add(point, instance);
            }

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, Enumerable.Empty<Vector2Int>(), points));
        }
        public void Add(Vector2Int point, int variantIndex)
        {
            var map = Dependencies.Get<IMap>();

            if (_objects.ContainsKey(point))
                return;

            _objects.Add(point, Variants[variantIndex].Instantiate(transform, point, map, _gridPositions, _gridRotations));

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, Enumerable.Empty<Vector2Int>(), new Vector2Int[] { point }));
        }
        public void Add(Vector2Int point, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, int? variantIndex = null)
        {
            var map = Dependencies.Get<IMap>();

            if (_objects.ContainsKey(point))
                return;

            var variant = variantIndex.HasValue ? Variants[variantIndex.Value] : Variants.Random();

            _objects.Add(point, variant.Instantiate(transform, localPosition, localRotation, localScale));

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, Enumerable.Empty<Vector2Int>(), new Vector2Int[] { point }));
        }
        public void Remove(Vector2Int point) => Remove(new Vector2Int[] { point });
        public void Remove(IEnumerable<Vector2Int> points)
        {
            var children = new List<GameObject>();
            foreach (var point in points)
            {
                if (_objects.ContainsKey(point) && !children.Contains(_objects[point]))
                    children.Add(_objects[point]);
            }

            foreach (var child in children)
            {
                _objects.Remove(_gridPositions.GetGridPoint(child.transform.position));
                Destroy(child);
            }

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, points, Enumerable.Empty<Vector2Int>()));
        }

        public GameObject GetObject(Vector2Int point) => _objects.GetValueOrDefault(point);
        public int GetVariantIndex(Vector2Int point)
        {
            return GetVariantIndex(GetObject(point).name);
        }
        public int GetVariantIndex(string name)
        {
            if (Variants == null || Variants.Length < 2)
                return 0;

            for (int i = 0; i < Variants.Length; i++)
            {
                if (name.StartsWith(Variants[i].Prefab.name))
                    return i;
            }

            return 0;
        }

        public void Clear()
        {
            _objects.ForEach(o => Destroy(o.Value));
            _objects.Clear();
        }

        public string GetName() => string.IsNullOrWhiteSpace(Name) ? name : Name;

        #region Saving
        [Serializable]
        public class StructureCollectionFloatData
        {
            public string Key;
            public StructureCollectionFloatVariantData[] Variants;
        }
        [Serializable]
        public class StructureCollectionFloatVariantData
        {
            public string Prefab;
            public Vector3[] Positions;
            public float[] Rotations;
            public float[] Scales;
            public string[] InstanceData;
        }

        public StructureCollectionFloatData SaveData()
        {
            var gridRotations = Dependencies.Get<IGridRotations>();
            var variants = new List<StructureCollectionFloatVariantData>();

            foreach (var variant in Variants)
            {
                var positions = new List<Vector3>();
                var rotations = new List<float>();
                var scales = new List<float>();

                foreach (var o in _objects)
                {
                    if (o.Value.name.StartsWith(variant.Prefab.name))
                    {
                        positions.Add(o.Value.transform.position);
                        rotations.Add(gridRotations.GetRotation(o.Value.transform));
                        scales.Add(o.Value.transform.localScale.x / variant.Prefab.transform.localScale.x);
                    }
                }

                if (positions.Count > 0)
                {
                    variants.Add(new StructureCollectionFloatVariantData()
                    {
                        Prefab = variant.Prefab.name,
                        Positions = positions.ToArray(),
                        Rotations = rotations.ToArray(),
                        Scales = scales.ToArray()
                    });
                }
            }

            return new StructureCollectionFloatData() { Key = Key, Variants = variants.ToArray() };
        }
        public void LoadData(StructureCollectionFloatData data)
        {
            var oldPoints = _objects.Keys.ToList();

            Clear();

            foreach (var variantData in data.Variants)
            {
                var variant = Variants.FirstOrDefault(p => p.Prefab.name.Equals(variantData.Prefab));
                if (variant == null)
                {
                    Debug.LogError($"Decorator {name} could not find prefab {variantData.Prefab}");
                    continue;
                }

                for (int i = 0; i < variantData.Positions.Length; i++)
                {
                    var position = variantData.Positions[i];
                    var rotation = variantData.Rotations[i];
                    var scale = variantData.Scales[i];

                    var instance = Instantiate(variant.Prefab, transform);
                    instance.transform.position = position;
                    instance.transform.localScale = variant.Prefab.transform.localScale * scale;
                    _gridRotations.SetRotation(instance.transform, rotation);
                    _objects.Add(_gridPositions.GetGridPoint(position), instance);
                }

                if (variantData.InstanceData != null && variantData.InstanceData.Length == _objects.Count)
                {
                    for (int i = 0; i < variantData.InstanceData.Length; i++)
                    {
                        _objects.ElementAt(i).Value.GetComponent<ISaveData>().LoadData(variantData.InstanceData[i]);
                    }
                }
            }


            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, oldPoints, GetPoints()));
        }
        #endregion
    }
}