using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that stores items and has storage walkers to manage items as follows:<br/>
    /// I:      fill up on items that have been configured as <see cref="StorageOrderMode.Get"/><br/>
    /// II:     deliver to <see cref="IItemReceiver"/> that need items<br/>
    /// III:    get rid of items that have been configured as <see cref="StorageOrderMode.Empty"/><br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_storage_walker_component.html")]
    public class StorageWalkerComponent : StorageComponent
    {
        public override string Key => "STG";

        [Tooltip("holds the storage walkers of this component that perform logistical jobs for it")]
        public ManualStorageWalkerSpawner StorageWalkers;

        private IGiverPathfinder _giverPathfinder;
        private IReceiverPathfinder _receiverPathfinder;

        private void Awake()
        {
            StorageWalkers.Initialize(Building, onFinished: storageWalkerReturned);
        }
        private void Start()
        {
            _giverPathfinder = Dependencies.Get<IGiverPathfinder>();
            _receiverPathfinder = Dependencies.Get<IReceiverPathfinder>();

            this.StartChecker(checkWorkers);
        }

        private IEnumerator checkWorkers()
        {
            if (!StorageWalkers.HasWalker)
                yield break;

            if (!Building.IsWorking)
                yield break;

            if (!Building.HasAccessPoint(StorageWalkers.Prefab.PathType, StorageWalkers.Prefab.PathTag))
                yield break;

            //GET
            foreach (var order in Orders.Where(o => o.Mode == StorageOrderMode.Get).OrderBy(o => o.Item.Priority))
            {
                var capacity = GetReceiveCapacityRemaining(order.Item);
                if (capacity <= 0)
                    continue;

                var giverPathQuery = _giverPathfinder.GetGiverPathQuery(Building, null, new ItemQuantity(order.Item, order.Item.UnitSize), StorageWalkers.Prefab.MaxDistance, StorageWalkers.Prefab.PathType, StorageWalkers.Prefab.PathTag);
                if (giverPathQuery == null)
                    continue;

                yield return null;
                yield return null;

                var giverPath = giverPathQuery.Complete();
                if (giverPath == null)
                    continue;

                StorageWalkers.Spawn(walker => walker.StartGet(giverPath, new ItemQuantity(order.Item, capacity)));
                yield break;
            }
            //SUPPLY
            foreach (var items in Storage.GetItemQuantities().OrderBy(i => i.Item.Priority).ToList())
            {
                var receiverPathQuery = _receiverPathfinder.GetReceiverPathQuery(Building, null, items, StorageWalkers.Prefab.MaxDistance, StorageWalkers.Prefab.PathType, StorageWalkers.Prefab.PathTag, Priority);
                if (receiverPathQuery == null)
                    continue;

                yield return null;
                yield return null;

                var receiverPath = receiverPathQuery.Complete();
                if (receiverPath == null)
                    continue;

                StorageWalkers.Spawn(walker => walker.StartSupply(receiverPath, Storage, items.Item));
                yield break;
            }
            //EMPTY
            foreach (var order in Orders.Where(o => o.Mode == StorageOrderMode.Empty).OrderBy(o => o.Item.Priority))
            {
                int quantity = Storage.GetItemsOverRatio(order.Item, order.Ratio);
                if (quantity <= 0)
                    continue;

                var receiverPathQuery = _receiverPathfinder.GetReceiverPathQuery(Building, null, new ItemQuantity(order.Item, quantity), StorageWalkers.Prefab.MaxDistance, StorageWalkers.Prefab.PathType, StorageWalkers.Prefab.PathTag);
                if (receiverPathQuery == null)
                    continue;

                yield return null;
                yield return null;

                var receiverPath = receiverPathQuery.Complete();
                if (receiverPath == null)
                    continue;

                StorageWalkers.Spawn(walker => walker.StartEmpty(receiverPath, Storage, order.Item, quantity));
                yield break;
            }
        }

        private void storageWalkerReturned(StorageWalker walker)
        {
            this.ReceiveAll(walker.Storage);
        }

        #region Saving
        [Serializable]
        public class StorageWalkerData
        {
            public ItemStorage.ItemStorageData Storage;
            public StorageOrder.StorageOrderData[] Orders;
            public ManualWalkerSpawnerData SpawnerData;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new StorageWalkerData()
            {
                Storage = Storage.SaveData(),
                Orders = Orders.Select(o => o.SaveData()).ToArray(),
                SpawnerData = StorageWalkers.SaveData()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<StorageWalkerData>(json);

            Storage.LoadData(data.Storage);
            if (data.Orders != null)
                Orders = data.Orders.Select(o => StorageOrder.FromData(o)).ToArray();
            StorageWalkers.LoadData(data.SpawnerData);
        }
        #endregion
    }
}