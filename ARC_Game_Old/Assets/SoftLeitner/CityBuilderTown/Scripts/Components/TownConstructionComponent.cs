using CityBuilderCore;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace CityBuilderTown
{
    /// <summary>
    /// building component used when constructing buildings<br/>
    /// when first placed it checks if there are any trees or rocks in the way and creates harvest tasks so they are replaced<br/>
    /// when all obstructions are cleared it creates <see cref="TownClearTask"/>s for every point of the building which removes grass details from the terrain<br/>
    /// if there are any items specified in <see cref="ItemStorage.ItemCapacities"/> it creates a <see cref="TownDeliverTask"/> for those next<br/>
    /// finally if there is a <see cref="BuildTaskPrefab"/> specified it creates that and waits for the building to be done<br/>
    /// after all the work is done it terminates itself and adds the building specified in <see cref="FinishedBuilding"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_construction_component.html")]
    public class TownConstructionComponent : BuildingComponent, IItemReceiver, IItemOwner
    {
        public enum ConstructionState
        {
            ClearStructures = 0,
            ClearGround = 1,
            DeliverItems = 10,
            Build = 20
        }

        [Tooltip("specify the items needed for construction in Item Capacities(optional)")]
        public ItemStorage ItemStorage;
        [Tooltip("optional prefab for the build task that is created at the build stage, walker count and build duration is specified in the task")]
        public TownBuildTask BuildTaskPrefab;
        [Tooltip("the building that is created when construction is finished and the construction building is terminated")]
        public BuildingInfo FinishedBuilding;
        [Tooltip("is raised towards 0 to visualize building progress")]
        public Transform BuildVisual;
        [Tooltip("gets disabled until structures and ground is cleared so walkers can walk on the building before it actually rises from the ground")]
        public NavMeshObstacle Obstacle;

        public override string Key => "CTR";

        public int Priority => 2000;

        public BuildingComponentReference<IItemReceiver> ReceiverReference { get; set; }
        public IItemContainer ItemContainer => ItemStorage;

        BuildingComponentReference<IItemReceiver> IBuildingTrait<IItemReceiver>.Reference { get => ReceiverReference; set => ReceiverReference = value; }

        private ConstructionState _state;
        private List<TownTask> _currentTasks;
        private float _height;

        private void Start()
        {
            if (BuildVisual)
                _height = -BuildVisual.localPosition.y;
            if (Obstacle)
                Obstacle.enabled = false;
        }

        private void Update()
        {
            if (_state == ConstructionState.Build && BuildVisual)
            {
                var buildTask = _currentTasks.OfType<TownBuildTask>().FirstOrDefault();
                if (buildTask != null)
                {
                    BuildVisual.localPosition = new Vector3(0, buildTask.Progress * _height - _height, 0);
                }
            }
        }

        public override void SetupComponent()
        {
            base.SetupComponent();

            _currentTasks = new List<TownTask>();

            if (Time.frameCount > 1)//cant check harvests on first frame because structures may not be initialized
            {
                foreach (var point in Building.GetPoints())
                {
                    if (TownManager.Instance.CheckHarvest(point))
                        _currentTasks.Add(TownManager.Instance.CreateHarvest(point, transform));
                }
            }
        }
        public override void InitializeComponent()
        {
            base.InitializeComponent();

            ReceiverReference = registerTrait<IItemReceiver>(this);

            this.StartChecker(checkConstruction);
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<IItemReceiver>(this);

            _currentTasks.ForEach(t => t.Terminate());
        }

        public override void SuspendComponent()
        {
            base.SuspendComponent();

            _currentTasks.ForEach(t => t.Terminate());
            _currentTasks.Clear();

            _state = ConstructionState.ClearStructures;
        }

        public IEnumerable<Item> GetReceiveItems() => _state == ConstructionState.DeliverItems ? ItemStorage.ItemCapacities.Select(i => i.Item) : Enumerable.Empty<Item>();
        public int GetReceiveCapacityRemaining(Item item) => _state == ConstructionState.DeliverItems ? ItemStorage.GetItemCapacityRemaining(item) : 0;
        public void ReserveCapacity(Item item, int quantity) => ItemStorage.ReserveCapacity(item, quantity);
        public void UnreserveCapacity(Item item, int quantity) => ItemStorage.UnreserveCapacity(item, quantity);
        public int Receive(ItemStorage storage, Item item, int quantity)
        {
            var remaining = quantity - storage.MoveItemsTo(ItemStorage, item, quantity);

            _currentTasks.OfType<TownDeliverTask>().ForEach(d => d.CheckDone());

            return remaining;
        }

        private void checkConstruction()
        {
            if (!Building.IsWorking)
                return;

            var townManager = TownManager.Instance;

            foreach (var point in Building.GetPoints())
            {
                if (townManager.GetTask(point) != null)
                    return;
                //work is still being done on the premises
                //just checking current tasks is not enough
                //because harvest tasks are managed globally
            }

            _currentTasks.Clear();

            switch (_state)
            {
                case ConstructionState.ClearStructures:
                    foreach (var point in Building.GetPoints())
                    {
                        _currentTasks.Add(townManager.CreateClear(point, transform));
                    }

                    _state = ConstructionState.ClearGround;
                    break;
                case ConstructionState.ClearGround:
                    if (ItemStorage.GetItemCapacity() > 0)
                        _currentTasks.Add(townManager.CreateDeliver(Building.Point, transform));
                    if (Obstacle)
                        Obstacle.enabled = true;
                    _state = ConstructionState.DeliverItems;
                    break;
                case ConstructionState.DeliverItems:
                    if (BuildTaskPrefab)
                        _currentTasks.Add(townManager.CreateTask(BuildTaskPrefab, Building.Point, transform));
                    _state = ConstructionState.Build;
                    break;
                case ConstructionState.Build:
                    Dependencies.GetOptional<INotificationManager>()?.Notify(new NotificationRequest($"Construction of a {FinishedBuilding.Name} has finished!", Building.WorldCenter));

                    Building.Terminate();

                    if (Building is ExpandableBuilding expandableBuilding)
                        Dependencies.Get<IBuildingManager>().Add(transform.position, transform.rotation, FinishedBuilding.GetPrefab(Building.Index), b => ((ExpandableBuilding)b).Expansion = expandableBuilding.Expansion);
                    else
                        Dependencies.Get<IBuildingManager>().Add(transform.position, transform.rotation, FinishedBuilding.GetPrefab(Building.Index));
                    break;
                default:
                    break;
            }
        }

        #region Saving
        [Serializable]
        public class TownConstructionData
        {
            public int State;
            public TownTaskData[] CurrentTasks;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new TownConstructionData()
            {
                State = (int)_state,
                CurrentTasks = _currentTasks.Where(t => t).Select(t => t.SaveData()).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<TownConstructionData>(json);

            _state = (ConstructionState)data.State;
            _currentTasks = data.CurrentTasks.Select(t => TownManager.Instance.CreateTask(t, transform)).ToList();

            if (Obstacle)
                Obstacle.enabled = _state >= ConstructionState.DeliverItems;
        }
        #endregion
    }
}
