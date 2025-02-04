using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace CityBuilderCore
{
    /// <summary>
    /// straightforward <see cref="IStructureManager"/> implementation<br/>
    /// holds collections, decorators and tiles in seperate lists similar to underlying structures
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/structures">https://citybuilder.softleitner.com/manual/structures</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_structure_manager.html")]
    public class DefaultStructureManager : MonoBehaviour, IStructureManager
    {
        public const int LEVEL_COUNT = 8;

        [Header("Map Pathing Options")]
        [Tooltip("whether a straight path from start to end is returned if no valid path is found")]
        public bool AllowInvalidPath = true;
        [Tooltip("area mask that is used when no other mask is defined through a WalkerAreaMask in the walker info tag, leave empty for all nav mesh areas")]
        public WalkerAreaMask DefaultAreaMask;
        [Header("Map Grid Pathing Options")]
        public GridPathfindingSettings PathfindingSettings;
        [Tooltip(@"advanced structure pathing options that let walkers only walk on points that have certain features
check out the test scene in CityBuilderCore.Tests/City/Movements/StructurePathDebugging")]
        public StructurePathOption[] PathOptions;

        public event Action Changed;

        private readonly Dictionary<int, StructureLevelManager> _levels = new Dictionary<int, StructureLevelManager>();

        private readonly List<StructureReference> _structures = new List<StructureReference>();
        private readonly List<StructureCollection> _collections = new List<StructureCollection>();
        private readonly List<StructureCollectionFloat> _collectionsFloat = new List<StructureCollectionFloat>();
        private readonly List<StructureDecorators> _decorators = new List<StructureDecorators>();
        private readonly List<StructureTiles> _tiles = new List<StructureTiles>();
        private readonly List<StructureReference> _underlying = new List<StructureReference>();

        private NavMeshPathfinding _navMesh = new NavMeshPathfinding();
        private StructurePaths _paths = new StructurePaths();

        protected virtual void Awake()
        {
            Dependencies.Register<IStructureManager>(this);
            Dependencies.Register<IMapPathfinder>(_navMesh);
            Dependencies.Register<IMapGridPathfinder>(_paths);
            Dependencies.Register<IMapGridLinker>(_paths);
        }

        protected virtual void Start()
        {
            _navMesh.Initialize(DefaultAreaMask?.AreaMask ?? NavMesh.AllAreas, AllowInvalidPath);
            _paths.Initialize(PathfindingSettings, PathOptions);
        }

        protected virtual void Update()
        {
            _navMesh.Calculate();
            _paths.Calculate();
        }

        protected virtual void OnDestroy()
        {
            _paths.Dispose();
        }

        public IEnumerable<IStructure> GetStructures(int mask)
        {
            if (mask == 0)
            {
                foreach (var structure in _structures)
                {
                    yield return structure.Instance;
                }

                foreach (var collectionFloat in _collectionsFloat)
                {
                    yield return collectionFloat;
                }

                foreach (var decorators in _decorators)
                {
                    yield return decorators;
                }

                foreach (var tiles in _tiles)
                {
                    yield return tiles;
                }

                foreach (var underlying in _underlying)
                {
                    yield return underlying.Instance;
                }
            }
            else
            {
                var levelStructures = new HashSet<IStructure>();
                foreach (var level in _levels)
                {
                    if (!StructureLevelMask.Check(mask, level.Key))
                        continue;

                    foreach (var structureReference in level.Value.GetStructureReferences())
                    {
                        if (!levelStructures.Contains(structureReference.Instance))
                        {
                            levelStructures.Add(structureReference.Instance);
                            yield return structureReference.Instance;
                        }
                    }
                }

                foreach (var collection in _collections.Where(c => c.Level.Check(mask)))
                {
                    yield return collection;
                }

                foreach (var collectionFloat in _collectionsFloat.Where(c => c.Level.Check(mask)))
                {
                    yield return collectionFloat;
                }

                foreach (var decorators in _decorators.Where(c => c.Level.Check(mask)))
                {
                    yield return decorators;
                }

                foreach (var tiles in _tiles.Where(c => c.Level.Check(mask)))
                {
                    yield return tiles;
                }

                foreach (var underlying in _underlying.Select(u => u.Instance).Where(c => StructureLevelMask.Check(c.Level, mask)))
                {
                    yield return underlying;
                }
            }
        }
        public IEnumerable<IStructure> GetStructures(Vector2Int point, int mask, bool? isWalkable = null, bool? isUnderlying = null, bool? isDecorator = null)
        {
            if (!isUnderlying.HasValue || !isUnderlying.Value)
            {
                List<IStructure> levelStructures = new List<IStructure>();
                foreach (var level in _levels)
                {
                    if (!StructureLevelMask.Check(mask, level.Key))
                        continue;

                    var structure = level.Value.GetStructure(point);
                    if (structure == null)
                        continue;

                    if (isDecorator.HasValue && structure.IsDecorator != isDecorator.Value)
                        continue;

                    if (isWalkable.HasValue && structure.IsWalkable != isWalkable.Value)
                        continue;

                    if (!levelStructures.Contains(structure))
                    {
                        levelStructures.Add(structure);
                        yield return structure;
                    }
                }

                foreach (var collection in _collections.Where(c => c.Level.Check(mask) && c.HasPoint(point) && (!isDecorator.HasValue || c.IsDecorator == isDecorator.Value) && (!isWalkable.HasValue || c.IsWalkable == isWalkable.Value)))
                {
                    yield return collection;
                }

                foreach (var collectionFloat in _collectionsFloat.Where(c => c.Level.Check(mask) && c.HasPoint(point) && (!isDecorator.HasValue || c.IsDecorator == isDecorator.Value) && (!isWalkable.HasValue || c.IsWalkable == isWalkable.Value)))
                {
                    yield return collectionFloat;
                }

                if ((!isDecorator.HasValue || isDecorator.Value) && (!isWalkable.HasValue || isWalkable.Value == false))
                {
                    foreach (var decorators in _decorators.Where(c => c.Level.Check(mask) && c.HasPoint(point)))
                    {
                        yield return decorators;
                    }
                }

                foreach (var tiles in _tiles.Where(c => c.Level.Check(mask) && c.HasPoint(point) && (!isDecorator.HasValue || c.IsDecorator == isDecorator.Value) && (!isWalkable.HasValue || c.IsWalkable == isWalkable.Value)))
                {
                    yield return tiles;
                }
            }

            if (!isUnderlying.HasValue || isUnderlying.Value)
            {
                foreach (var underlying in _underlying.Select(u => u.Instance).Where(c => StructureLevelMask.Check(c.Level, mask) && c.HasPoint(point) && (!isDecorator.HasValue || c.IsDecorator == isDecorator.Value) && (!isWalkable.HasValue || c.IsWalkable == isWalkable.Value)))
                {
                    yield return underlying;
                }
            }
        }

        public IStructure GetStructure(string key)
        {
            var collection = GetStructureCollection(key);
            if (collection)
                return collection;
            
            var collectionFloat = GetStructureCollectionFloat(key);
            if (collectionFloat)
                return collectionFloat;

            var decorator = GetStructureDecorators(key);
            if (decorator)
                return decorator;

            var tiles = GetStructureTiles(key);
            if (tiles)
                return tiles;

            var underlying = _underlying.FirstOrDefault(u => u.Instance.Key == key);
            if (underlying?.Instance != null)
                return underlying.Instance;

            return null;
        }
        public StructureCollection GetStructureCollection(string key) => _collections.FirstOrDefault(c => c.Key == key);
        public StructureCollectionFloat GetStructureCollectionFloat(string key) => _collectionsFloat.FirstOrDefault(c => c.Key == key);
        public StructureDecorators GetStructureDecorators(string key) => _decorators.FirstOrDefault(c => c.Key == key);
        public StructureTiles GetStructureTiles(string key) => _tiles.FirstOrDefault(c => c.Key == key);

        public int Remove(IEnumerable<Vector2Int> points, int mask, bool decoratorsOnly, Action<IStructure> removing = null)
        {
            List<StructureReference> structures = new List<StructureReference>();

            foreach (var point in points)
            {
                foreach (var structure in GetStructures(point, mask, null, false, decoratorsOnly ? (bool?)true : null))
                {
                    if (!structures.Contains(structure.StructureReference))
                        structures.Add(structure.StructureReference);
                }
            }

            if (structures.Count == 0)
            {
                foreach (var point in points)
                {
                    foreach (var structure in GetStructures(point, mask, null, true))
                    {
                        if (!structures.Contains(structure.StructureReference))
                            structures.Add(structure.StructureReference);
                    }
                }
            }

            foreach (var structure in structures)
            {
                if (!structure.Instance.IsDestructible)
                    continue;
                if (decoratorsOnly && !structure.Instance.IsDecorator)
                    continue;

                removing?.Invoke(structure.Instance);

                structure.Instance.Remove(points);
            }

            return structures.Count;
        }

        public void RegisterStructure(IStructure structure, bool isUnderlying = false)
        {
            _paths.Initialize(PathfindingSettings, PathOptions);

            structure.PointsChanged += structurePointsChanged;
            structure.StructureReference.Replacing += structureReferenceReplacing;

            if (structure is StructureCollection collection)
            {
                _collections.Add(collection);
            }
            else if (structure is StructureCollectionFloat collectionFloat)
            {
                _collectionsFloat.Add(collectionFloat);
            }
            else if (structure is StructureDecorators decorators)
            {
                _decorators.Add(decorators);
            }
            else if (structure is StructureTiles tiles)
            {
                _tiles.Add(tiles);
            }
            else if (isUnderlying)
            {
                _underlying.Add(structure.StructureReference);
            }
            else
            {
                _structures.Add(structure.StructureReference);
                foreach (var manager in getManagers(structure.Level))
                {
                    manager.AddStructure(structure);
                }
            }

            checkPoints(structure.GetPoints());

            Changed?.Invoke();
        }

        public void DeregisterStructure(IStructure structure, bool isUnderlying = false)
        {
            structure.PointsChanged -= structurePointsChanged;
            structure.StructureReference.Replacing -= structureReferenceReplacing;

            if (structure is StructureCollection collection)
            {
                _collections.Remove(collection);
            }
            else if (structure is StructureCollectionFloat collectionFloat)
            {
                _collectionsFloat.Remove(collectionFloat);
            }
            else if (structure is StructureDecorators decorators)
            {
                _decorators.Remove(decorators);
            }
            else if (structure is StructureTiles tiles)
            {
                _tiles.Remove(tiles);
            }
            else if (isUnderlying)
            {
                _underlying.Remove(structure.StructureReference);
            }
            else
            {
                _structures.Remove(structure.StructureReference);
                foreach (var manager in getManagers(structure.Level))
                {
                    manager.RemoveStructure(structure);
                }
            }

            checkPoints(structure.GetPoints());

            Changed?.Invoke();
        }

        private IEnumerable<StructureLevelManager> getManagers(int level)
        {
            if (level == 0)
            {
                if (!_levels.ContainsKey(0))
                    _levels.Add(0, new StructureLevelManager());
                yield return _levels[0];
            }
            else
            {
                int pow = 1;
                for (int i = 0; i < LEVEL_COUNT; i++)
                {
                    if ((level & pow) == pow)
                    {
                        if (!_levels.ContainsKey(pow))
                            _levels.Add(pow, new StructureLevelManager());
                        yield return _levels[pow];
                    }
                    pow *= 2;
                }
            }
        }

        private void structurePointsChanged(PointsChanged<IStructure> change)
        {
            checkPoints(change.AddedPoints);
            checkPoints(change.RemovedPoints);

            Changed?.Invoke();
        }

        private void structureReferenceReplacing(IStructure a, IStructure b)
        {
            a.PointsChanged -= structurePointsChanged;
            b.PointsChanged += structurePointsChanged;
        }

        private void checkPoints(IEnumerable<Vector2Int> points)
        {
            _paths.CheckPoints(this, points);
        }

        #region Saving
        [Serializable]
        public class StructuresData
        {
            public StructureCollection.StructureCollectionData[] Collections;
            public StructureCollectionFloat.StructureCollectionFloatData[] CollectionsFloat;
            public StructureDecorators.StructureDecoratorsData[] Decorators;
            public StructureTiles.StructureTilesData[] Tiles;
        }

        public string SaveData()
        {
            return JsonUtility.ToJson(new StructuresData()
            {
                Collections = _collections.Where(c => c.IsDestructible).Select(c => c.SaveData()).ToArray(),
                CollectionsFloat = _collectionsFloat.Where(c => c.IsDestructible).Select(c => c.SaveData()).ToArray(),
                Decorators = _decorators.Select(c => c.SaveData()).ToArray(),
                Tiles = _tiles.Where(t => t.IsDestructible).Select(t => t.SaveData()).ToArray(),
            });
        }
        public void LoadData(string json)
        {
            var structuresData = JsonUtility.FromJson<StructuresData>(json);

            if (structuresData.Collections != null)
            {
                foreach (var data in structuresData.Collections)
                {
                    var collection = _collections.FirstOrDefault(c => c.Key == data.Key);
                    if (collection == null)
                        continue;

                    collection.LoadData(data);
                }
            }

            if (structuresData.CollectionsFloat != null)
            {
                foreach (var data in structuresData.CollectionsFloat)
                {
                    var collection = _collectionsFloat.FirstOrDefault(c => c.Key == data.Key);
                    if (collection == null)
                        continue;

                    collection.LoadData(data);
                }
            }

            if (structuresData.Decorators != null)
            {
                foreach (var data in structuresData.Decorators)
                {
                    var decorators = _decorators.FirstOrDefault(c => c.Key == data.Key);
                    if (decorators == null)
                        continue;

                    decorators.LoadData(data);
                }
            }

            if (structuresData.Tiles != null)
            {
                foreach (var data in structuresData.Tiles)
                {
                    var tiles = _tiles.FirstOrDefault(c => c.Key == data.Key);
                    if (tiles == null)
                        continue;

                    tiles.LoadData(data);
                }
            }
        }
        #endregion
    }
}