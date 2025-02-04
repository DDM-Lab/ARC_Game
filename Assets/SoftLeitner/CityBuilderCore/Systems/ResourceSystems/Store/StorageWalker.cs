using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// walker for storage functions:<br/>
    /// Supplies <see cref="IItemReceiver"/> | Gets items from <see cref="IItemGiver"/> | empties items into <see cref="IItemReceiver"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_storage_walker.html")]
    public class StorageWalker : Walker, IItemOwner
    {
        public enum StorageWalkerState
        {
            Inactive = 0,
            Leaving = 1,
            Waiting = 2,
            Returning = 3,
        }
        public enum StorageWalkerMode
        {
            Get = 10,
            Supply = 20,
            Empty = 30
        }

        [Tooltip("storage that holds the items the walker is moving around")]
        public ItemStorage Storage;
        [Tooltip("maximum distance from home to the target as the crow flies")]
        public float MaxDistance = 100;
        [Tooltip("whether the walker has to return home before becoming available again")]
        public bool ReturnHome = true;

        public override ItemStorage ItemStorage => Storage;
        public IItemContainer ItemContainer => Storage;

        private StorageWalkerMode _mode;
        private StorageWalkerState _state = StorageWalkerState.Inactive;
        private BuildingComponentReference<IItemReceiver> _receiver;
        private BuildingComponentReference<IItemGiver> _giver;
        private ItemQuantity _items;

        public override void Initialize(BuildingReference home, Vector2Int start)
        {
            base.Initialize(home, start);

            _state = StorageWalkerState.Inactive;
        }

        public void StartSupply(BuildingComponentPath<IItemReceiver> receiverPath, ItemStorage storage, Item item)
        {
            _mode = StorageWalkerMode.Supply;
            _state = StorageWalkerState.Leaving;

            _receiver = receiverPath.Component;

            _start = receiverPath.Path.StartPoint;

            storage.MoveItemsTo(Storage, item, receiverPath.Component.Instance.GetReceiveCapacityRemaining(item));

            Walk(receiverPath.Path, arrive);
        }
        public void StartGet(BuildingComponentPath<IItemGiver> giverPath, ItemQuantity items)
        {
            _mode = StorageWalkerMode.Get;
            _state = StorageWalkerState.Leaving;

            _giver = giverPath.Component;

            _items = items;

            _start = giverPath.Path.StartPoint;

            Walk(giverPath.Path, arrive);
        }
        public void StartEmpty(BuildingComponentPath<IItemReceiver> receiverPath, ItemStorage storage, Item item, int maxQuantity)
        {
            _mode = StorageWalkerMode.Empty;
            _state = StorageWalkerState.Leaving;

            _receiver = receiverPath.Component;

            _start = receiverPath.Path.StartPoint;

            storage.MoveItemsTo(Storage, item, maxQuantity);

            Walk(receiverPath.Path, arrive);
        }

        private void arrive()
        {
            switch (_mode)
            {
                case StorageWalkerMode.Get:
                    _giver.Instance.Give(Storage, _items.Item, _items.Quantity);
                    break;
                case StorageWalkerMode.Empty:
                case StorageWalkerMode.Supply:
                    _receiver.Instance.ReceiveAll(Storage);
                    break;
            }

            if (ReturnHome)
            {
                returnHome();
            }
            else
            {
                onFinished();
            }
        }

        private void returnHome()
        {
            _state = StorageWalkerState.Waiting;
            tryWalk(() => PathHelper.FindPathQuery(_current, Home.Instance, PathType, PathTag), planned: () => _state = StorageWalkerState.Returning);
        }

        protected override void onFinished()
        {
            _state = StorageWalkerState.Inactive;
            base.onFinished();
        }

        public override string GetDescription()
        {
            List<object> parameters = new List<object>();

            parameters.Add(Home.Instance.GetName());

            switch (_state)
            {
                case StorageWalkerState.Leaving:
                    switch (_mode)
                    {
                        case StorageWalkerMode.Get:
                            parameters.Add(_giver.Instance.Building.GetName());
                            break;
                        case StorageWalkerMode.Supply:
                        case StorageWalkerMode.Empty:
                            parameters.Add(_receiver.Instance.Building.GetName());
                            break;
                        default:
                            break;
                    }
                    break;
            }

            return getDescription((int)_state, parameters.ToArray());
        }

        public override string GetDebugText() => Storage.GetDebugText();

        #region Saving
        [Serializable]
        public class StorageWalkerData
        {
            public WalkerData WalkerData;
            public ItemStorage.ItemStorageData Storage;
            public int Mode;
            public int State;
            public BuildingComponentReferenceData Home;
            public BuildingComponentReferenceData Receiver;
            public BuildingComponentReferenceData Giver;
            public ItemQuantity.ItemQuantityData Items;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new StorageWalkerData()
            {
                WalkerData = savewalkerData(),
                Storage = Storage.SaveData(),
                Mode = (int)_mode,
                State = (int)_state,
                Receiver = _receiver?.GetData(),
                Giver = _giver?.GetData(),
                Items = _items?.GetData()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<StorageWalkerData>(json);

            loadWalkerData(data.WalkerData);

            Storage.LoadData(data.Storage);

            _mode = (StorageWalkerMode)data.Mode;
            _state = (StorageWalkerState)data.State;

            _receiver = data.Receiver.GetReference<IItemReceiver>();
            _giver = data.Giver.GetReference<IItemGiver>();
            _items = ItemQuantity.FromData(data.Items);

            switch (_state)
            {
                case StorageWalkerState.Leaving:
                    ContinueWalk(arrive);
                    break;
                case StorageWalkerState.Returning:
                    ContinueWalk();
                    break;
                case StorageWalkerState.Waiting:
                    returnHome();
                    break;
            }
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualStorageWalkerSpawner : ManualWalkerSpawner<StorageWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicStorageWalkerSpawner : CyclicWalkerSpawner<StorageWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledStorageWalkerSpawner : PooledWalkerSpawner<StorageWalker> { }
}