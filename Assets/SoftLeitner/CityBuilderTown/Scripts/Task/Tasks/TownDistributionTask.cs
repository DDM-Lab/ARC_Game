using CityBuilderCore;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// makes a walker go to the market the task stands on and pick up items<br/>
    /// the walker then roams the roads connected to the market filling up their stores<br/>
    /// afterwards the walker walks back to the market and returns the remaining items<br/>
    /// the items carried by the walker are reserved in the market to avoid the market getting filled while the walker is out
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_distribution_task.html")]
    public class TownDistributionTask : TownTask
    {
        [Tooltip("how many points the walker will memorize try to avoid")]
        public int Memory = 64;
        [Tooltip("how many steps the walker roams before returning")]
        public int Range = 16;
        [Tooltip("maximum quantity per item the walker can take")]
        public int MaxQuantity = 10;
        [Tooltip("time between trips")]
        public float CooldownDuration;

        public override IEnumerable<TownWalker> Walkers
        {
            get
            {
                if (_walker != null)
                    yield return _walker;
            }
        }

        private TownMarketComponent _market;
        public TownMarketComponent Market
        {
            get
            {
                if (_market == null)
                    _market = Dependencies.Get<IBuildingManager>().GetBuilding(Point).First().GetBuildingComponent<TownMarketComponent>();
                return _market;
            }
        }

        private TownWalker _walker;
        private float _cooldown;
        private List<ItemQuantity> _reserved;

        private void Start()
        {
            _cooldown = CooldownDuration;
        }

        protected override void Update()
        {
            base.Update();

            if (Visual)
                Visual.gameObject.SetActive(_walker == null && _cooldown <= 0f && Market.Storage.HasItems() && Market.Building.HasAccessPoint(PathType.RoadBlocked));

            if (_walker == null)
                _cooldown -= Time.deltaTime;
        }

        public override bool CanStartTask(TownWalker walker)
        {
            return _walker == null && !walker.ItemStorage.HasItems() && _cooldown <= 0f && Market.Storage.HasItems() && Market.Building.HasAccessPoint(PathType.RoadBlocked);
        }
        public override WalkerAction[] StartTask(TownWalker walker)
        {
            _walker = walker;
            _walker.Moved += walkerMoved;
            _cooldown = CooldownDuration;
            _reserved = new List<ItemQuantity>();

            foreach (var item in Market.Items)
            {
                var stored = Market.Storage.GetItemQuantity(item);
                var capacity = walker.Storage.GetItemCapacity(item);
                var quantity = Mathf.Min(stored, capacity, MaxQuantity);
                if (quantity == 0)
                    continue;

                Market.Storage.ReserveCapacity(item, quantity);
                _reserved.Add(new ItemQuantity(item, quantity));
            }

            return new WalkerAction[]
            {
                new WalkPointAction(Market.Building.GetAccessPoint(PathType.RoadBlocked).Value),
                new TakeItemsAction(Market.Building.BuildingReference,_reserved,false),
                new RoamActionTyped(Memory,Range, PathType.RoadBlocked),
                new WalkPointActionTyped(Market.Building.GetAccessPoint(PathType.RoadBlocked).Value, PathType.Road),
            };
        }
        public override void ContinueTask(TownWalker walker)
        {
            _walker = walker;
            _walker.Moved += walkerMoved;
        }

        public override void FinishTask(TownWalker walker, ProcessState process)
        {
            foreach (var itemQuantity in _reserved)
            {
                Market.Storage.UnreserveCapacity(itemQuantity.Item, itemQuantity.Quantity);
            }

            _reserved = null;

            foreach (var item in Market.Items)
            {
                _walker.Storage.MoveItemsTo(Market.Storage, item);
            }

            _walker.Moved -= walkerMoved;
            _walker = null;

            if (process.IsCanceled)
                return;

            //walker.Storage.AddItems(Items.Item, Items.Quantity);
        }

        public override string GetDescription() => "distributing wood and food to houses";
        public override string GetDebugText() => _cooldown.ToString();

        private void walkerMoved(Walker walker)
        {
            var buildingManager = Dependencies.Get<IBuildingManager>();
            IEnumerable<Vector2Int> points = PositionHelper.GetAdjacent(walker.CurrentPoint, Vector2Int.one);

            foreach (var areaPosition in points)
            {
                foreach (var home in buildingManager.GetBuilding(areaPosition).SelectMany(b => b.GetBuildingComponents<TownHomeComponent>()))
                {
                    home.FillRecipient(walker.ItemStorage);
                }
            }
        }

        #region Saving
        public class TownBuildTaskData
        {
            public float Cooldown;
            public ItemQuantity.ItemQuantityData[] Reserved;
        }

        protected override string saveExtras()
        {
            return JsonUtility.ToJson(new TownBuildTaskData()
            {
                Cooldown = _cooldown,
                Reserved = _reserved?.Select(r => r.GetData()).ToArray()
            });
        }
        protected override void loadExtras(string json)
        {
            base.loadExtras(json);

            var data = JsonUtility.FromJson<TownBuildTaskData>(json);

            _cooldown = data.Cooldown;
            _reserved = data.Reserved?.Select(r => r.GetItemQuantity()).ToList();
        }
        #endregion
    }
}
