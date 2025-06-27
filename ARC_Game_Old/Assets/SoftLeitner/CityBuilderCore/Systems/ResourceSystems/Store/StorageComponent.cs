using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that stores items<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_storage_component.html")]
    public class StorageComponent : BuildingComponent, IStorageComponent
    {
        public override string Key => "SOG";

        [Tooltip("container that contains the items, can be configured for various scenarios")]
        public ItemStorage Storage;
        [Tooltip("defines which items are stored in the component and how they are handled")]
        public StorageOrder[] Orders;

        public int Priority => 100;

        ItemStorage IStorageComponent.Storage => Storage;
        StorageOrder[] IStorageComponent.Orders => Orders;

        public BuildingComponentReference<IStorageComponent> Reference { get; set; }
        public BuildingComponentReference<IItemReceiver> ReceiverReference { get; set; }
        public BuildingComponentReference<IItemGiver> GiverReference { get; set; }

        BuildingComponentReference<IItemGiver> IBuildingTrait<IItemGiver>.Reference { get => GiverReference; set => GiverReference = value; }
        BuildingComponentReference<IItemReceiver> IBuildingTrait<IItemReceiver>.Reference { get => ReceiverReference; set => ReceiverReference = value; }

        public IItemContainer ItemContainer => Storage;

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Reference = registerTrait<IStorageComponent>(this);
            ReceiverReference = registerTrait<IItemReceiver>(this);
            GiverReference = registerTrait<IItemGiver>(this);

            if (Building is ExpandableBuilding expandableBuilding)
            {
                if (Storage.IsStackedStorage)
                    Storage.StackCount *= expandableBuilding.Expansion.x * expandableBuilding.Expansion.y;
                else
                    Storage.Capacity *= expandableBuilding.Expansion.x * expandableBuilding.Expansion.y;
            }
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            var storageReplacement = replacement.GetBuildingComponent<IStorageComponent>();

            replaceTrait(this, storageReplacement);
            replaceTrait<IItemReceiver>(this, storageReplacement);
            replaceTrait<IItemGiver>(this, storageReplacement);
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<IStorageComponent>(this);
            deregisterTrait<IItemReceiver>(this);
            deregisterTrait<IItemGiver>(this);
        }

        public override string GetDebugText() => Storage.GetDebugText();

        public IEnumerable<Item> GetReceiveItems() => Orders.Select(o => o.Item);
        public int GetReceiveCapacityRemaining(Item item)
        {
            if (!Building.IsWorking)
                return 0;

            foreach (var order in Orders)
            {
                if (order.Item == item)
                {
                    if (order.Ratio == 0f)
                        return 0;
                    return Storage.GetItemCapacityRemaining(item, order.Ratio);
                }
            }
            return 0;
        }
        public void ReserveCapacity(Item item, int quantity)
        {
            Storage.ReserveCapacity(item, quantity);
        }
        public void UnreserveCapacity(Item item, int quantity)
        {
            Storage.UnreserveCapacity(item, quantity);
        }
        public int Receive(ItemStorage storage, Item item, int quantity)
        {
            if (!Building.IsWorking)
                return quantity;

            return quantity - storage.MoveItemsTo(Storage, item, quantity);
        }

        public int GetGiveQuantity(Item item)
        {
            if (!Building.IsWorking)
                return 0;

            return Storage.GetItemQuantityRemaining(item);
        }
        public void ReserveQuantity(Item item, int quantity)
        {
            Storage.ReserveQuantity(item, quantity);
        }
        public void UnreserveQuantity(Item item, int quantity)
        {
            Storage.UnreserveQuantity(item, quantity);
        }
        public int Give(ItemStorage storage, Item item, int quantity)
        {
            if (!Building.IsWorking)
                return 0;

            return quantity - Storage.MoveItemsTo(storage, item, quantity);
        }

        #region Saving
        [Serializable]
        public class StorageData
        {
            public ItemStorage.ItemStorageData Storage;
            public StorageOrder.StorageOrderData[] Orders;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new StorageData()
            {
                Storage = Storage.SaveData(),
                Orders = Orders.Select(o => o.SaveData()).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<StorageData>(json);

            Storage.LoadData(data.Storage);
            Orders = data.Orders.Select(o => StorageOrder.FromData(o)).ToArray();
        }
        #endregion
    }
}