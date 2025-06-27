using CityBuilderCore;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace CityBuilderTown
{
    /// <summary>
    /// central manager for the town demo<br/>
    /// manages tasks, jobs, walkers, seasons<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_manager.html")]
    public class TownManager : ExtraDataBehaviour
    {
        public static TownManager Instance;

        public static int WorkParameter = Animator.StringToHash("work");
        public static int ChopParameter = Animator.StringToHash("chop");

        [Header("Items")]
        [Tooltip("item category that contains all the items walkers can consume in their home to refill their food")]
        public ItemCategory Food;
        [Tooltip("item that is used in homes so walkers can visit them to refill warmth")]
        public Item Wood;
        [Tooltip("limits how many of some items should be produced(not all logs should be turned into firewood), can be changed by players and is persisted")]
        public ItemQuantity[] ItemLimits;

        [Header("Structure")]
        [Tooltip("parent for newly spawned walkers")]
        public Transform WalkerParent;
        [Tooltip("parent for newly created tasks")]
        public Transform TaskParent;

        [Header("Sets")]
        [Tooltip("set of all tasks")]
        public TownTaskSet TownTasks;
        [Tooltip("set of all jobs")]
        public TownJobSet TownJobs;

        [Header("Other")]
        [Tooltip("timing unit for seasons(0-3)")]
        public TimingUnit SeasonUnit;
        [Tooltip("spawner for town walkers")]
        public ManualTownWalkerSpawner Walkers;

        [Header("UI")]
        [Tooltip("tool that is activated when the game is first started so players can place their initial buildings an walkers")]
        public TownStartupTool StartupTool;
        [Tooltip("fader used to hide map generation when the map is loaded")]
        public Fader Fader;
        [Tooltip("text element that shows the total count of walkers")]
        public TMPro.TMP_InputField TotalText;
        [Tooltip("text element that shows how many walkers dont have a specific job assigned")]
        public TMPro.TMP_InputField JoblessText;

        [Header("Map")]
        [FormerlySerializedAs("DebugFoodModifier")]
        [Tooltip("modifies how fast food is lost over time")]
        public float MapFoodModifier = 1f;
        [FormerlySerializedAs("DebugWarmModifier")]
        [Tooltip("modifies how fast warmth is lost over time")]
        public float MapWarmModifier = 1f;
        [FormerlySerializedAs("DebugColdModifier")]
        [Tooltip("modifies how cold it is(affects warmth loss and wood usage)")]
        public float MapColdModifier = 1f;
        [FormerlySerializedAs("DebugAgeModifier")]
        [Tooltip("modifies how fast walkers age")]
        public float MapAgeModifier = 1f;
        [FormerlySerializedAs("DebugGrowthModifier")]
        [Tooltip("modifies how many trees and bushes grow at the start of summer")]
        public float MapGrowthModifier = 1f;

        [Header("Debug")]
        [Tooltip("no walkers spawn when true, helpful for tests and debug")]
        public bool DebugSuppressWalkers;

        public float FoodModifier => MapFoodModifier * _difficulty.FoodModifier;
        public float WarmModifier => MapWarmModifier * _difficulty.WarmModifier;
        public float ColdModifier => MapColdModifier * _difficulty.ColdModifier;
        public float AgeModifier => MapAgeModifier * _difficulty.AgeModifier;
        public float GrowthModifier => MapGrowthModifier * _difficulty.GrowthModifier;

        public TownSeason Season => _season;
        public float Coldness
        {
            get
            {
                switch (_season)
                {
                    case TownSeason.Spring: return 1f * ColdModifier;
                    case TownSeason.Summer: return 0f * ColdModifier;
                    case TownSeason.Autumn: return 1f * ColdModifier;
                    case TownSeason.Winter: return 2f * ColdModifier;
                    default: return 0f;
                }
            }
        }

        private HashSet<Vector2Int> _harvestPoints;
        private Dictionary<TownHarvestTask, IStructure> _harvestTasks;

        private bool _isStartup;
        private TownDifficulty _difficulty;

        private IGridPositions _gridPositions;
        private IStructureManager _structureManager;
        private IGameSpeed _gameSpeed;

        private TownSeason _season;
        private List<TownTask> _tasks = new List<TownTask>();
        private TownClearTask _clearTask;
        private TownBuildTask _buildTask;
        private TownDeliverTask _deliverTask;

        private void Awake()
        {
            Instance = this;
            Dependencies.Register(this);

            Walkers.Initialize(WalkerParent, onFinished: walkerDied);
        }

        private void Start()
        {
            _gridPositions = Dependencies.Get<IGridPositions>();
            _structureManager = Dependencies.Get<IStructureManager>();
            _gameSpeed = Dependencies.Get<IGameSpeed>();

            _difficulty = Dependencies.Get<IMissionManager>().MissionParameters?.Difficulty as TownDifficulty ?? ScriptableObject.CreateInstance<TownDifficulty>();

            _clearTask = TownTasks.Objects.OfType<TownClearTask>().First();
            _buildTask = TownTasks.Objects.OfType<TownBuildTask>().First();
            _deliverTask = TownTasks.Objects.OfType<TownDeliverTask>().First();

            this.FindObjects<TownWalker>().ForEach(f => Walkers.Integrate(f));

            setJobless();
            setTotal();
        }

        private void Update()
        {
            var season = (TownSeason)SeasonUnit.GetIndex(_gameSpeed.Playtime);
            if (season != _season)
                changeSeason(season);
        }

        public void LoadingChanged(bool isLoading)
        {
            if (!isLoading)
            {
                this.FindObjects<TownJobInput>().ForEach(i => i.SetText());
                this.FindObjects<TownLimitInput>().ForEach(i => i.SetText());
            }
        }

        public int GetItemLimit(Item item) => ItemLimits.Single(l => l.Item = item).Quantity;
        public void SetItemLimit(Item item, int quantity) => ItemLimits.Single(l => l.Item == item).Quantity = quantity;
        public bool GetItemLimitReached(Item item)
        {
            var limit = ItemLimits.FirstOrDefault(l => l.Item == item);
            if (limit == null)
                return false;

            var storageQuantity = item.GetStoredQuantity();
            var taskQuantitiy = _tasks.OfType<TownItemTask>().Where(t => t.Items.Item == item).Sum(t => t.Items.Quantity);
            var walkerQuantity = Walkers.CurrentWalkers.Sum(w => w.ItemStorage.GetItemQuantity(item));

            var totalQuantity = storageQuantity + taskQuantitiy + walkerQuantity;

            return totalQuantity >= limit.Quantity;
        }

        public void RegisterTask(TownTask task)
        {
            if (_tasks.Contains(task))
                return;
            _tasks.Add(task);
        }
        public void DeregisterTask(TownTask task)
        {
            _tasks.Remove(task);
        }

        public TownTask CreateTask(TownTaskData taskData, Transform parent = null)
        {
            var task = CreateTask(TownTasks.GetObject(taskData.Key), taskData.Point, parent);

            task.LoadData(taskData);

            return task;
        }
        public T CreateTask<T>(T prefab, Vector2Int point, Transform parent = null) where T : TownTask
        {
            var task = Instantiate(prefab, _gridPositions.GetWorldCenterPosition(point), Quaternion.identity, parent ?? TaskParent);

            Dependencies.GetOptional<IGridHeights>()?.ApplyHeight(task.transform);

            return task;
        }

        public TownTask GetTask(Vector2Int point)
        {
            return _tasks.FirstOrDefault(t => t.Point == point);
        }
        public TownTask GetTask(Guid id)
        {
            return _tasks.FirstOrDefault(t => t.Id == id);
        }
        public TownTask GetTask(TownWalker townWalker)
        {
            if (townWalker.Job)
            {
                var jobTask = _tasks.OrderBy(t => Vector2Int.Distance(townWalker.GridPoint, t.Point)).FirstOrDefault(t => t.Job == townWalker.Job && t.CanStartTask(townWalker));
                if (jobTask != null)
                    return jobTask;
            }

            return _tasks.OrderBy(t => Vector2Int.Distance(townWalker.GridPoint, t.Point)).FirstOrDefault(t => t.Job == null && t.CanStartTask(townWalker));
        }

        public BuildingReference GetHome(TownWalker _)
        {
            return Dependencies.Get<IBuildingManager>().GetBuildingTraits<TownHomeComponent>()
                .Where(h => !h.Building.IsSuspended && h.RemainingWalkerCapacity > 0)
                .OrderBy(h => h.RemainingWalkerCapacity)
                .FirstOrDefault()?.Building.BuildingReference;
        }
        public void SpawnWalker(IBuilding building)
        {
            if (DebugSuppressWalkers)
                return;

            Walkers.Spawn(start: PathHelper.FindRandomPoint(building.Point, 1, 2, Walkers.Prefab.PathType, Walkers.Prefab.PathTag));

            setJobless();
            setTotal();
        }

        public bool CheckTask(Vector2Int point) => _tasks.Any(t => t.Point == point);

        public bool CheckHarvest(Vector2Int point)
        {
            initializeHarvest();
            return _harvestPoints.Contains(point) && !CheckTask(point);
        }
        public TownHarvestTask CreateHarvest(Vector2Int point, Transform parent = null)
        {
            initializeHarvest();
            return CreateTask(_harvestTasks.First(t => t.Value.HasPoint(point)).Key, point, parent);
        }

        public TownClearTask CreateClear(Vector2Int point, Transform parent = null) => CreateTask(_clearTask, point, parent);
        public TownBuildTask CreateBuild(Vector2Int point, Transform parent = null) => CreateTask(_buildTask, point, parent);
        public TownDeliverTask CreateDeliver(Vector2Int point, Transform parent = null) => CreateTask(_deliverTask, point, parent);

        public bool Demolish(IEnumerable<Vector2Int> points)
        {
            var tasks = points.Select(p => GetTask(p)).Where(p => p != null && p.transform.parent == TaskParent).ToList();

            if (tasks.Any())
            {
                tasks.ForEach(t => t.Terminate());
                return true;
            }

            foreach (var point in points)
            {
                if (_tasks.OfType<TownDemolishTask>().Any(t => t.Point == point) || tasks.Any(t => t.Point == point))
                    continue;

                foreach (var structure in _structureManager.GetStructures(point, 1))
                {
                    var taskPoint = point;
                    if (structure is IBuilding building)
                        taskPoint = building.Point;

                    if (_tasks.OfType<TownDemolishTask>().Any(t => t.Point == taskPoint) || tasks.Any(t => t.Point == taskPoint))
                        continue;

                    var prefab = TownTasks.Objects.OfType<TownDemolishTask>().Where(t => t.StructureKey == structure.Key).FirstOrDefault();
                    if (prefab == null)
                        prefab = TownTasks.Objects.OfType<TownDemolishTask>().Where(t => string.IsNullOrWhiteSpace(t.StructureKey)).FirstOrDefault();

                    if (prefab == null)
                        continue;

                    tasks.Add(CreateTask(prefab, taskPoint));
                    break;
                }
            }

            return tasks.Any();
        }

        public int GetJobCount(TownJob job)
        {
            return Walkers.CurrentWalkers.Count(w => w.Job == job);
        }
        public void SetJobCount(TownJob job, int count)
        {
            var currentCount = GetJobCount(job);

            if (count > currentCount)
            {
                for (int i = 0; i < (count - currentCount); i++)
                {
                    Walkers.CurrentWalkers.First(w => w.Job == null).SetJob(job);
                }
            }
            else if (count < currentCount)
            {
                for (int i = 0; i < (currentCount - count); i++)
                {
                    Walkers.CurrentWalkers.First(w => w.Job == job).SetJob(null);
                }
            }

            setJobless();
        }

        public void Startup()
        {
            _isStartup = true;

            foreach (Transform tool in StartupTool.transform.parent)
            {
                tool.gameObject.SetActive(false);
            }

            StartupTool.gameObject.SetActive(true);
            StartupTool.GetComponent<UnityEngine.UI.Toggle>().isOn = true;
        }
        public void StartupSet()
        {
            _isStartup = false;

            foreach (Transform tool in StartupTool.transform.parent)
            {
                tool.gameObject.SetActive(true);
            }

            StartupTool.GetComponent<UnityEngine.UI.Toggle>().isOn = false;
            StartupTool.gameObject.SetActive(false);
        }

        private void initializeHarvest()
        {
            if (_harvestPoints != null)
                return;

            _harvestPoints = new HashSet<Vector2Int>();
            _harvestTasks = new Dictionary<TownHarvestTask, IStructure>();

            foreach (var task in TownTasks.Objects.OfType<TownHarvestTask>())
            {
                if (task.Job)
                    continue;

                var structure = _structureManager.GetStructure(task.PrimaryStructureKey);

                _harvestTasks.Add(task, structure);

                foreach (var point in structure.GetPoints())
                {
                    _harvestPoints.Add(point);
                }

                structure.PointsChanged += harvestPointsChanged;
            }
        }
        private void harvestPointsChanged(PointsChanged<IStructure> change)
        {
            foreach (var point in change.RemovedPoints)
            {
                _harvestPoints.Remove(point);
            }
            foreach (var point in change.AddedPoints)
            {
                _harvestPoints.Add(point);
            }
        }

        private void walkerDied(TownWalker walker)
        {
            this.FindObjects<TownJobInput>().ForEach(i => i.SetText());
            setJobless();
            setTotal();
        }

        private void setTotal()
        {
            TotalText.text = Walkers.CurrentWalkers.Count().ToString();
        }

        private void setJobless()
        {
            JoblessText.text = Walkers.CurrentWalkers.Where(w => !w.Job).Count().ToString();
        }

        private void changeSeason(TownSeason season)
        {
            _season = season;

            switch (Season)
            {
                case TownSeason.Spring:
                    Dependencies.GetOptional<INotificationManager>()?.Notify(new NotificationRequest($"It is Spring now!"));
                    break;
                case TownSeason.Summer:
                    Dependencies.GetOptional<INotificationManager>()?.Notify(new NotificationRequest($"Summer has arrived!"));

                    if (GrowthModifier > 0f)
                    {
                        var trees = _structureManager.GetStructure("TRE");
                        var treesGrowing = _structureManager.GetStructure("TREG");

                        //turn growing trees into full trees
                        var grownTrees = treesGrowing.GetPoints().ToList();

                        //dont carry over randomization for trees, size is completely different
                        //small growing tree does not need to turn into small grown tree
                        IStructure.ReplacePoints(treesGrowing, trees, grownTrees, keepRandomization: false);

                        //place 10 new growing trees
                        treesGrowing.Add(_structureManager.GetRandomAvailablePoints(Mathf.RoundToInt(10 * GrowthModifier)));

                        //regrow berries
                        var berries = _structureManager.GetStructure("BER");
                        var berriesEmpty = _structureManager.GetStructure("BERE");

                        var emptyBerries = berriesEmpty.GetPoints().ToList();

                        //carry over randomization and variant for bushes
                        //they need to look identical except for the berries
                        IStructure.ReplacePoints(berriesEmpty, berries, emptyBerries);

                        berriesEmpty.Add(_structureManager.GetRandomAvailablePoints(Mathf.RoundToInt(5 * GrowthModifier)));
                    }
                    break;
                case TownSeason.Autumn:
                    Dependencies.GetOptional<INotificationManager>()?.Notify(new NotificationRequest($"Autumn has started!"));
                    break;
                case TownSeason.Winter:
                    Dependencies.GetOptional<INotificationManager>()?.Notify(new NotificationRequest($"Winter has come!"));
                    break;
            }
        }

        #region Saving
        [Serializable]
        public class TownManagerData
        {
            public bool IsStartup;
            public ItemQuantity.ItemQuantityData[] ItemLimits;
            public TownTaskData[] IndependentTasks;
            public ManualWalkerSpawnerData Walkers;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new TownManagerData()
            {
                IsStartup = _isStartup,
                ItemLimits = ItemLimits.Select(i => i.GetData()).ToArray(),
                IndependentTasks = _tasks.OfType<TownTask>().Where(t => t.transform.parent == TaskParent).Select(t => t.SaveData()).ToArray(),
                Walkers = Walkers.SaveData()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<TownManagerData>(json);

            ItemLimits = data.ItemLimits.Select(i => i.GetItemQuantity()).ToArray();

            foreach (var task in _tasks.OfType<TownTask>().Where(t => t.transform.parent == TaskParent).ToList())
            {
                task.Terminate();
            }

            data.IndependentTasks.ForEach(d => CreateTask(d));

            Walkers.LoadData(data.Walkers);

            setJobless();
            setTotal();

            _isStartup = data.IsStartup;
            if (_isStartup)
                Startup();
        }
        #endregion
    }
}