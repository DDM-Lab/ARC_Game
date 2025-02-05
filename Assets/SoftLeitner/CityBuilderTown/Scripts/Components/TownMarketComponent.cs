using CityBuilderCore;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// component that has food and wood delivered to it and distributes them to any recipients connected by road
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_market_component.html")]
    public class TownMarketComponent : BuildingComponent, IItemOwner, IItemReceiver
    {
        [Tooltip("stores the wood and food that will be distributed to connected recipients")]
        public ItemStorage Storage;
        [Tooltip("task that takes care of items being supplied to the market, quantities are specified in the task")]
        public TownDeliverTask DeliverTask;
        [Tooltip("tasks that distribute the items to connected recipients(create multiple to have multiple walkers distribute at the same time)")]
        public TownDistributionTask[] DistributionTasks;

        public override string Key => "MKT";

        public IItemContainer ItemContainer => Storage;

        public int Priority => 2000;

        public IEnumerable<Item> Items
        {
            get
            {
                foreach (var food in TownManager.Instance.Food.Items)
                {
                    yield return food;
                }

                yield return TownManager.Instance.Wood;
            }
        }

        public BuildingComponentReference<IItemReceiver> Reference { get; set; }

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Reference = registerTrait<IItemReceiver>(this);
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            replaceTrait(this, replacement.GetBuildingComponent<IItemReceiver>());
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<IItemReceiver>(this);

            DeliverTask.Terminate();
            DistributionTasks.ForEach(t => t.Terminate());
        }

        public override void SuspendComponent()
        {
            base.SuspendComponent();

            DeliverTask.SuspendTask();
            DistributionTasks.ForEach(t => t.SuspendTask());
        }
        public override void ResumeComponent()
        {
            base.ResumeComponent();

            DeliverTask.ResumeTask();
            DistributionTasks.ForEach(t => t.ResumeTask());
        }

        public IEnumerable<Item> GetReceiveItems() => Items;
        public int GetReceiveCapacityRemaining(Item item) => (Building.IsWorking && Items.Contains(item)) ? Storage.GetItemCapacityRemaining(item) : 0;
        public void ReserveCapacity(Item item, int quantity) => Storage.ReserveCapacity(item, quantity);
        public void UnreserveCapacity(Item item, int quantity) => Storage.UnreserveCapacity(item, quantity);
        public int Receive(ItemStorage storage, Item item, int quantity) => quantity - storage.MoveItemsTo(Storage, item, quantity);

        public override string GetDebugText() => Storage.GetDebugText();

        #region Saving
        [Serializable]
        public class TownMarketData
        {
            public ItemStorage.ItemStorageData Storage;
            public TownTaskData DeliverTask;
            public TownTaskData[] DistributionTasks;
        }

        public override string SaveData()
        {
            var data = new TownMarketData();

            data.Storage = Storage.SaveData();
            data.DeliverTask = DeliverTask.SaveData();
            data.DistributionTasks = DistributionTasks.Select(t => t.SaveData()).ToArray();

            return JsonUtility.ToJson(data);
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<TownMarketData>(json);

            Storage.LoadData(data.Storage);
            DeliverTask.LoadData(data.DeliverTask);

            for (int i = 0; i < data.DistributionTasks.Length; i++)
            {
                DistributionTasks[i].LoadData(data.DistributionTasks[i]);
            }
        }
        #endregion
    }
}
