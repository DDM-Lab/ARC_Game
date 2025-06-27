using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// manager behaviour that takes care of buildings, walkers and their bars
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_building_manager.html")]
    public class DefaultBuildingManager : MonoBehaviour, IBuildingManager, IWalkerManager, IBarManager
    {
        [Tooltip("optional addon that is put on buildings when they are placed, THREE uses a BuildingAddonTransformer here to make buildings pop up in an animation")]
        public BuildingAddon AddingAddon;
        [Tooltip("optional addon that is put on buildings when they are suspended")]
        public BuildingAddon SuspensionAddon;

        [Tooltip("when a building fulfills the requirements for the next evolution stage this delay is counted down before the building evolves")]
        public float EvolutionDelay;
        [Tooltip("addon that is put on buildings while the EvolutionDelay is active(some Particles in THREE)")]
        public BuildingAddon EvolutionDelayAddon;
        [Tooltip("when a building no longer fulfills the requirements for its current evolution stage this delay is counted down before the building actually devolves")]
        public float DevolutionDelay;
        [Tooltip("addon that is put on buildings while the DevolutionDelay is active(some Particles in THREE)")]
        public BuildingAddon DevolutionDelayAddon;

        [Tooltip("parent transform for global bars(health bars in defense that are put on the canvas)")]
        public Transform BarRoot;

        public event Action<IBuilding> Added;
        public event Action<IBuilding> Registered;
        public event Action<IBuilding> Deregistered;

        public event Action<Walker> WalkerRegistered;
        public event Action<Walker> WalkerDeregistered;

        private readonly List<BuildingReference> _buildings = new List<BuildingReference>();
        private readonly Dictionary<Type, List<object>> _traits = new Dictionary<Type, List<object>>();
        private readonly List<Walker> _walkers = new List<Walker>();

        private List<BuildingValueBars> _buildingBars = new List<BuildingValueBars>();
        private List<WalkerValueBars> _walkerBars = new List<WalkerValueBars>();

        private LazyDependency<IStructureManager> _structureManager = new LazyDependency<IStructureManager>();

        protected virtual void Awake()
        {
            Dependencies.Register<IBuildingManager>(this);
            Dependencies.Register<IWalkerManager>(this);
            Dependencies.Register<IBarManager>(this);
        }

        protected virtual void Update()
        {
            foreach (var bar in _buildingBars)
            {
                bar.Update();
            }
        }

        public T Add<T>(Vector3 position, Quaternion rotation, T prefab, Action<T> initializer = null) where T : Building
        {
            var building = Instantiate(prefab, position, rotation, transform);

            initializer?.Invoke(building);

            building.Index = prefab.Info.GetPrefabIndex(prefab);

            building.Setup();
            building.Initialize();

            _structureManager.Value.Remove(building.GetPoints(), prefab.Info.Level.Value, true);

            if (AddingAddon)
                building.AddAddon(AddingAddon);

            Added?.Invoke(building);

            return building;
        }

        public int Count(BuildingInfo info) => _buildings.Where(b => b.Instance.Info == info).Count();
        public int Count(BuildingCategory category) => _buildings.Where(b => category.Contains(b.Instance.Info)).Count();

        public IEnumerable<IBuilding> GetBuildings() => _buildings.Select(b => b.Instance);
        public IEnumerable<IBuilding> GetBuildings(BuildingCategory category) => _buildings.Select(b => b.Instance).Where(b => category.Contains(b.Info));
        public IEnumerable<IBuilding> GetBuildings(BuildingInfo info) => _buildings.Where(b => b.Instance.Info == info).Select(b => b.Instance);

        public void RegisterBuilding(IBuilding building)
        {
            _buildings.Add(building.BuildingReference);

            foreach (var bar in _buildingBars)
            {
                bar.Add(building.BuildingReference);
            }

            Registered?.Invoke(building);
        }
        public void DeregisterBuilding(IBuilding building)
        {
            _buildings.Remove(building.BuildingReference);

            foreach (var bar in _buildingBars)
            {
                bar.Remove(building.BuildingReference);
            }

            Deregistered?.Invoke(building);
        }

        public Walker GetWalker(Guid id)=>_walkers.FirstOrDefault(w=>w.Id == id);
        public IEnumerable<Walker> GetWalkers() => _walkers;

        public void RegisterWalker(Walker walker)
        {
            _walkers.Add(walker);

            foreach (var bar in _walkerBars)
            {
                bar.Add(walker);
            }

            WalkerRegistered?.Invoke(walker);
        }
        public void DeregisterWalker(Walker walker)
        {
            _walkers.Remove(walker);

            foreach (var bar in _walkerBars)
            {
                bar.Remove(walker);
            }

            WalkerDeregistered?.Invoke(walker);
        }

        public IEnumerable<IBuilding> GetBuilding(Vector2Int point)
        {
            return _structureManager.Value.GetStructures(point, 0, null, false, false).OfType<IBuilding>();
        }
        public BuildingReference GetBuildingReference(Guid id) => _buildings.FirstOrDefault(b => b.Instance.Id == id);

        public BuildingComponentReference<T> RegisterBuildingTrait<T>(T component)
            where T : IBuildingTrait<T>
        {
            var type = typeof(T);
            if (!_traits.ContainsKey(type))
                _traits.Add(type, new List<object>());
            var reference = new BuildingComponentReference<T>(component);
            _traits[type].Add(reference);
            return reference;
        }
        public void ReplaceBuildingTrait<T>(T component, T replacement)
            where T : IBuildingTrait<T>
        {
            if (replacement == null)
            {
                component.Reference.Instance = default;
                DeregisterBuildingTrait(component);
            }
            else
            {
                component.Reference.Instance = replacement;
                replacement.Reference = component.Reference;
            }
        }
        public void DeregisterBuildingTrait<T>(T component)
            where T : IBuildingTrait<T>
        {
            var type = typeof(T);
            if (!_traits.ContainsKey(type))
                return;
            _traits[type].Remove(component.Reference);
        }

        public IEnumerable<T> GetBuildingTraits<T>()
            where T : IBuildingTrait<T>
        {
            var type = typeof(T);
            if (!_traits.ContainsKey(type))
                return Enumerable.Empty<T>();
            return _traits[type].Cast<BuildingComponentReference<T>>().Select(r => r.Instance);
        }

        public void ActivateBar(ViewBuildingBarBase view)
        {
            var bar = new BuildingValueBars(view, BarRoot);
            foreach (var building in _buildings)
            {
                bar.Add(building);
            }
            _buildingBars.Add(bar);
        }
        public void ActivateBar(ViewWalkerBarBase view)
        {
            var bar = new WalkerValueBars(view, BarRoot);
            foreach (var walker in _walkers)
            {
                bar.Add(walker);
            }
            _walkerBars.Add(bar);
        }
        public void DeactivateBar(ViewBuildingBarBase view)
        {
            var bar = _buildingBars.FirstOrDefault(b => b.View == view);
            if (bar == null)
                return;

            bar.Clear();
            _buildingBars.Remove(bar);
        }
        public void DeactivateBar(ViewWalkerBarBase view)
        {
            var bar = _walkerBars.FirstOrDefault(b => b.View == view);
            if (bar == null)
                return;

            bar.Clear();
            _walkerBars.Remove(bar);
        }

        public bool HasEvolutionDelay(bool direction)
        {
            if (direction)
                return EvolutionDelay > 0f;
            else
                return DevolutionDelay > 0f;
        }
        public float GetEvolutionDelay(bool direction)
        {
            if (direction)
                return EvolutionDelay;
            else
                return DevolutionDelay;
        }
        public string AddEvolutionAddon(IBuilding building, bool direction)
        {
            BuildingAddon addon = direction ? EvolutionDelayAddon : DevolutionDelayAddon;

            if (addon)
                return building.AddAddon(addon).Key;
            else
                return null;
        }

        public string AddSuspensionAddon(IBuilding building)
        {
            if (SuspensionAddon)
                return building.AddAddon(SuspensionAddon).Key;
            else
                return null;
        }

        #region Saving
        [Serializable]
        public class BuildingsData
        {
            public BuildingMetaData[] Buildings;
        }
        [Serializable]
        public class BuildingMetaData
        {
            public string Id;
            public string Key;
            public int Index;
            public Vector2Int Point;
            public int Rotation;
            public string Data;
        }

        public string SaveData()
        {
            return JsonUtility.ToJson(new BuildingsData()
            {
                Buildings = _buildings.Select(b =>
                {
                    BuildingMetaData metaData = new BuildingMetaData
                    {
                        Id = b.Instance.Id.ToString(),
                        Key = b.Instance.Info.Key,
                        Index = b.Instance.Index,
                        Point = b.Instance.Point,
                        Rotation = b.Instance.Rotation.State,
                        Data = b.Instance.SaveData()
                    };

                    return metaData;
                }).ToArray()
            });
        }
        public void LoadData(string json)
        {
            var buildingsData = JsonUtility.FromJson<BuildingsData>(json);

            foreach (var building in _buildings.ToList())
            {
                building.Instance.Terminate();
            }

            var buildingDatas = new List<(IBuilding building, string data)>();
            var buildings = Dependencies.Get<IKeyedSet<BuildingInfo>>();

            foreach (var metaData in buildingsData.Buildings)
            {
                var info = buildings.GetObject(metaData.Key);
                if (info == null)
                    continue;

                var building = info.Create(metaData, transform);
                if (building == null)
                    continue;

                buildingDatas.Add((building, metaData.Data));
            }

            foreach (var (building, data) in buildingDatas)
            {
                building.LoadData(data);
            }
        }
        #endregion
    }
}