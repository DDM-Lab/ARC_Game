using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// walker that tries to find an <see cref="IItemReceiver"/> for the items it is given and deliver them<br/>
    /// if it cant find a receiver it will idle until the <see cref="Walker.MaxWait"/> runs out and the items perish
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_delivery_walker.html")]
    public class DeliveryWalker : Walker
    {
        public enum DeliveryWalkerState
        {
            Inactive = 0,
            WaitingDelivery = 1,
            Delivering = 2,
            WaitingReturn = 3,
            Returning = 4,
        }

        [Tooltip("storage the walker stores the items that it tries to deliver")]
        public ItemStorage Storage;
        [Tooltip("maximum distance to receiver as the crow flies")]
        public float MaxDistance = 100;
        [Tooltip("whether the walker has to go back home after delivering its items before it is available again")]
        public bool ReturnHome = true;

        public override ItemStorage ItemStorage => Storage;

        private DeliveryWalkerState _state = DeliveryWalkerState.Inactive;
        private BuildingComponentReference<IItemReceiver> _receiver;

        public override void Initialize(BuildingReference home, Vector2Int start)
        {
            base.Initialize(home, start);

            _state = DeliveryWalkerState.Inactive;
        }

        public BuildingComponentPath<IItemReceiver> GetReceiverPath(ItemQuantity items, IBuilding building, Vector2Int? start)
        {
            return Dependencies.Get<IReceiverPathfinder>().GetReceiverPath(building, start, items, MaxDistance, PathType, PathTag);
        }
        public BuildingComponentPathQuery<IItemReceiver> GetReceiverPathQuery(ItemQuantity items, IBuilding building, Vector2Int? start)
        {
            return Dependencies.Get<IReceiverPathfinder>().GetReceiverPathQuery(building, start, items, MaxDistance, PathType, PathTag);
        }

        public void StartDelivery(ItemStorage storage)
        {
            storage.MoveItemsTo(Storage);

            tryDeliver();
        }
        public void StartDelivery(ItemStorage storage, Item item)
        {
            storage.MoveItemsTo(Storage, item);

            tryDeliver();
        }
        public void StartDelivery(ItemStorage storage, BuildingComponentPath<IItemReceiver> componentPath)
        {
            storage.MoveItemsTo(Storage);

            _receiver = componentPath.Component;
            _state = DeliveryWalkerState.Delivering;

            Walk(componentPath.Path, finished: deliver);
        }
        public void StartDelivery(ItemStorage storage, Item item, BuildingComponentPath<IItemReceiver> componentPath)
        {
            storage.MoveItemsTo(Storage, item);

            _receiver = componentPath.Component;
            _state = DeliveryWalkerState.Delivering;

            Walk(componentPath.Path, finished: deliver);
        }

        private void deliver()
        {
            _start = _current;
            _receiver.Instance.ReceiveAll(Storage);

            if (Storage.HasItems())
            {
                tryDeliver();
            }
            else
            {
                if (ReturnHome)
                {
                    tryReturn();
                }
                else
                {
                    onFinished();
                }
            }
        }

        private void tryDeliver()
        {
            _state = DeliveryWalkerState.WaitingDelivery;
            tryWalk(() => GetReceiverPathQuery(Storage.GetItemQuantities().FirstOrDefault(), Home.Instance, _current),
              planned: c =>
              {
                  _receiver = c.Component;
                  _state = DeliveryWalkerState.Delivering;
              },
              finished: deliver, canceled: onFinished);
        }

        private void tryReturn()
        {
            _state = DeliveryWalkerState.WaitingReturn;
            tryWalk(Home.Instance, planned: () => _state = DeliveryWalkerState.Returning, finished: onFinished);
        }

        protected override void onFinished()
        {
            _state = DeliveryWalkerState.Inactive;
            base.onFinished();
        }

        public override string GetDescription()
        {
            List<object> parameters = new List<object>();

            parameters.Add(Home.Instance.GetName());

            switch (_state)
            {
                case DeliveryWalkerState.WaitingDelivery:
                    parameters.Add(Storage.GetItemNames());
                    break;
                case DeliveryWalkerState.Delivering:
                    parameters.Add(Storage.GetItemNames());
                    parameters.Add(_receiver.Instance.Building.GetName());
                    break;
            }

            return getDescription((int)_state, parameters.ToArray());
        }

        public override string GetDebugText() => Storage.GetDebugText();

        #region Saving
        [Serializable]
        public class DeliveryWalkerData
        {
            public WalkerData WalkerData;
            public int State;
            public BuildingComponentReferenceData Receiver;
            public ItemStorage.ItemStorageData Storage;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new DeliveryWalkerData()
            {
                WalkerData = savewalkerData(),
                Storage = Storage.SaveData(),
                State = (int)_state,
                Receiver = _receiver?.GetData()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<DeliveryWalkerData>(json);

            loadWalkerData(data.WalkerData);

            Storage.LoadData(data.Storage);

            _state = (DeliveryWalkerState)data.State;
            _receiver = data.Receiver.GetReference<IItemReceiver>();

            StartCoroutine(loadDelayed());//make sure storages have been loaded
        }

        private IEnumerator loadDelayed()
        {
            yield return new WaitForEndOfFrame();

            switch (_state)
            {
                case DeliveryWalkerState.WaitingDelivery:
                    tryDeliver();
                    break;
                case DeliveryWalkerState.Delivering:
                    ContinueWalk(deliver);
                    break;
                case DeliveryWalkerState.WaitingReturn:
                    tryReturn();
                    break;
                case DeliveryWalkerState.Returning:
                    ContinueWalk();
                    break;
                default:
                    break;
            }
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualDeliveryWalkerSpawner : ManualWalkerSpawner<DeliveryWalker>
    {
        [Tooltip(@"- Instant
find path before spawn
no spawn if none is found
- Prepared
prepares a path query before spawning
no spawn happens if none is found
- Delayed
walker spawns and takes items and 
looks for path while delay runs")]
        public WalkerInitializationMode InitializationMode = WalkerInitializationMode.Delayed;

        public void StartDeliver(MonoBehaviour owner, ItemStorage storage, Vector2Int? accessPoint)
        {
            switch (InitializationMode)
            {
                case WalkerInitializationMode.Instant:
                    var path = Prefab.GetReceiverPath(storage.GetItemQuantities().FirstOrDefault(), _building, null);
                    if (path != null)
                        Spawn(walker => walker.StartDelivery(storage, path), accessPoint);
                    break;
                case WalkerInitializationMode.Prepared:
                    Spawn(owner,
                        () => Prefab.GetReceiverPathQuery(storage.GetItemQuantities().FirstOrDefault(), _building, null),
                        q => q.Complete(),
                        (w, p) => w.StartDelivery(storage, p));
                    break;
                default:
                    Spawn(walker => walker.StartDelivery(storage), accessPoint);
                    break;
            }
        }
        public void StartDeliver(MonoBehaviour owner, ItemStorage storage, ItemQuantity items, Vector2Int? accessPoint)
        {
            switch (InitializationMode)
            {
                case WalkerInitializationMode.Instant:
                    var path = Prefab.GetReceiverPath(items, _building, null);
                    if (path != null)
                        Spawn(walker => walker.StartDelivery(storage, items.Item, path), accessPoint);
                    break;
                case WalkerInitializationMode.Prepared:
                    Spawn(owner,
                        () => Prefab.GetReceiverPathQuery(storage.GetItemQuantities().FirstOrDefault(), _building, null),
                        q => q.Complete(),
                        (w, p) => w.StartDelivery(storage, p));
                    break;
                default:
                    Spawn(walker => walker.StartDelivery(storage, items.Item), accessPoint);
                    break;
            }
        }
    }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicDeliveryWalkerSpawner : CyclicWalkerSpawner<DeliveryWalker>
    {

    }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledDeliveryWalkerSpawner : PooledWalkerSpawner<DeliveryWalker> { }
}