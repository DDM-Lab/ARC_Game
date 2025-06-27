using CityBuilderCore;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// houses <see cref="TownWalker"/>s and stores food and wood<br/>
    /// items are filled by the inhabitants or peddlers that work in a market that is connected via road<br/>
    /// food is taken by by walkers to restore their food value<br/>
    /// wood is periodically consumed by the component, as long as there is any walkers can restore their warmth value when they visit the house<br/>
    /// the home also periodically spawns walkers up to its capacity
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_home_component.html")]
    public class TownHomeComponent : BuildingComponent, IBuildingTrait<TownHomeComponent>, IItemRecipient
    {
        [Tooltip("stores food and wood needed to keep walkers alive")]
        public ItemStorage ItemStorage;
        [Tooltip("home will spawn new walkers until it has this many inhabitants")]
        public int WalkerCapacity;
        [Tooltip("interval between creating new walkers")]
        public float WalkerInterval;
        [Tooltip("wood is consumed in this interval(opposed to food walkers dont take wood, they can warm up as long as there is any)")]
        public float WoodInterval;

        [Tooltip("fired whenever items change, parameter is whether food is currently in storage")]
        public BoolEvent HasFoodChanged;
        [Tooltip("fired whenever items change, parameter is whether wood is currently in storage")]
        public BoolEvent HasWoodChanged;

        public override string Key => "HOM";

        public BuildingComponentReference<TownHomeComponent> Reference { get; set; }

        public List<TownWalker> Inhabitants { get; private set; }

        public int RemainingWalkerCapacity => Building.IsWorking ? WalkerCapacity - Inhabitants.Count : 0;

        public IItemContainer ItemContainer => ItemStorage;

        public bool HasFood => TownManager.Instance.Food.Items.Any(i => ItemStorage.HasItem(i));
        public bool HasWood => ItemStorage.HasItem(TownManager.Instance.Wood);

        private float _walkerGrowth;
        private float _woodUsage;

        private void Start()
        {
            onItemsChanged();
        }

        private void Update()
        {
            updateGrowth();
            updateFire();
        }

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Reference = registerTrait(this);

            if (Inhabitants == null)
                Inhabitants = new List<TownWalker>();
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            var replacementHome = replacement.GetBuildingComponent<TownHomeComponent>();

            if (replacementHome)
                replacementHome.Inhabitants = Inhabitants;

            replaceTrait(this, replacementHome);
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait(this);
        }

        public override void SuspendComponent()
        {
            base.SuspendComponent();

            Inhabitants.ForEach(i => i.Home = null);
            Inhabitants.Clear();
        }

        public void AddInhabitant(TownWalker walker)
        {
            Inhabitants.Add(walker);
        }
        public void RemoveInhabitant(TownWalker walker)
        {
            Inhabitants.Remove(walker);
        }

        public bool Eat()
        {
            if(!HasFood) 
                return false;
            ItemStorage.RemoveItems(TownManager.Instance.Food, 1);
            onItemsChanged();
            return true;
        }
        public bool Warm() => ItemStorage.HasItem(TownManager.Instance.Wood);

        public IEnumerable<Item> GetRecipientItems()
        {
            foreach (var item in TownManager.Instance.Food.Items)
            {
                yield return item;
            }

            yield return TownManager.Instance.Wood;
        }

        public IEnumerable<ItemQuantity> GetRecipientItemQuantities() => ItemStorage.GetItemQuantities();
        public void FillRecipient(ItemStorage itemStorage)
        {
            TownManager.Instance.Food.Items.ForEach(i => itemStorage.MoveItemsTo(ItemStorage, i));
            itemStorage.MoveItemsTo(ItemStorage, TownManager.Instance.Wood);

            onItemsChanged();
        }

        public override string GetDebugText()
        {
            return ItemStorage.GetDebugText() +
                "G:" + _walkerGrowth;
        }

        private void updateGrowth()
        {
            //if (Inhabitants.Count > 1 && RemainingWalkerCapacity > 0)
            if (RemainingWalkerCapacity > 0)
            {
                _walkerGrowth += Time.deltaTime * Building.Efficiency;
                if (_walkerGrowth >= WalkerInterval)
                {
                    _walkerGrowth = 0f;
                    TownManager.Instance.SpawnWalker(Building);
                }
            }
            else
            {
                _walkerGrowth = 0f;
            }
        }

        private void updateFire()
        {
            _woodUsage += Time.deltaTime * TownManager.Instance.Coldness;
            if (_woodUsage >= WoodInterval)
            {
                _woodUsage = 0f;
                ItemStorage.RemoveItems(TownManager.Instance.Wood, 1);
                onItemsChanged();
            }
        }

        private void onItemsChanged()
        {
            HasFoodChanged?.Invoke(HasFood);
            HasWoodChanged?.Invoke(HasWood);
        }

        #region Saving
        [Serializable]
        public class TomeHomeData
        {
            public ItemStorage.ItemStorageData Storage;
            public float WalkerGrowth;
            public float WoodUsage;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new TomeHomeData()
            {
                Storage = ItemStorage.SaveData(),
                WalkerGrowth = _walkerGrowth,
                WoodUsage = _woodUsage
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<TomeHomeData>(json);

            ItemStorage.LoadData(data.Storage);

            _walkerGrowth = data.WalkerGrowth;
            _woodUsage = data.WoodUsage;

            onItemsChanged();
        }
        #endregion
    }
}
