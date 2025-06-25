using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// walker that gets items from an <see cref="IItemGiver"/> to an <see cref="IDistributionComponent"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_purchase_walker.html")]
    public class PurchaseWalker : Walker, IItemOwner
    {
        public enum PurchaseWalkerState
        {
            Inactive = 0,
            ToGiver = 1,
            Waiting = 2,
            Returning = 3,
        }

        [Tooltip("storage the walker places items in while walking back from the item giver, the size is important to how fast a distributor can stock")]
        public ItemStorage Storage;
        [Tooltip("maximum distance from home to giver as the crow flies")]
        public float MaxDistance = 100;

        public override ItemStorage ItemStorage => Storage;
        public IItemContainer ItemContainer => Storage;
        public ItemQuantity Order => _order;

        private PurchaseWalkerState _state;
        private ItemQuantity _order;
        private BuildingComponentReference<IItemGiver> _giver;

        public override void Initialize(BuildingReference home, Vector2Int start)
        {
            base.Initialize(home, start);

            _state = PurchaseWalkerState.Inactive;
        }

        public void StartPurchase(BuildingComponentPath<IItemGiver> giverPath, ItemQuantity order)
        {
            _giver = giverPath.Component;
            _order = order;

            _start = giverPath.Path.StartPoint;
            _state = PurchaseWalkerState.ToGiver;

            Walk(giverPath.Path, purchase);
        }

        private void purchase()
        {
            _giver.Instance.Give(Storage, _order.Item, _order.Quantity);
            returnHome();
        }

        private void returnHome()
        {
            _state = PurchaseWalkerState.Waiting;
            tryWalk(() => PathHelper.FindPathQuery(_current, _start, PathType, PathTag), planned: () => _state = PurchaseWalkerState.Returning);
        }

        protected override void onFinished()
        {
            _state = PurchaseWalkerState.Inactive;
            base.onFinished();
        }

        public override string GetDescription()
        {
            List<object> parameters = new List<object>();

            parameters.Add(Home.Instance.GetName());

            switch (_state)
            {
                case PurchaseWalkerState.ToGiver:
                case PurchaseWalkerState.Waiting:
                case PurchaseWalkerState.Returning:
                    parameters.Add(_giver.Instance.Building.GetName());
                    parameters.Add(_order.Item.Name);
                    break;
            }

            return getDescription((int)_state, parameters.ToArray());
        }

        public override string GetDebugText() => Storage.GetDebugText();

        #region Saving
        [Serializable]
        public class PurchaseWalkerData
        {
            public WalkerData WalkerData;
            public int State;
            public ItemStorage.ItemStorageData Storage;
            public ItemQuantity.ItemQuantityData Order;
            public BuildingComponentReferenceData Giver;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new PurchaseWalkerData()
            {
                WalkerData = savewalkerData(),
                Storage = Storage.SaveData(),
                State = (int)_state,
                Order = _order.GetData(),
                Giver = _giver.GetData()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<PurchaseWalkerData>(json);

            loadWalkerData(data.WalkerData);

            Storage.LoadData(data.Storage);

            _state = (PurchaseWalkerState)data.State;
            _order = ItemQuantity.FromData(data.Order);
            _giver = data.Giver.GetReference<IItemGiver>();

            switch (_state)
            {
                case PurchaseWalkerState.ToGiver:
                    ContinueWalk(purchase);
                    break;
                case PurchaseWalkerState.Waiting:
                    returnHome();
                    break;
                case PurchaseWalkerState.Returning:
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
    public class ManualPurchaseWalkerSpawner : ManualWalkerSpawner<PurchaseWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicPurchaseWalkerSpawner : CyclicWalkerSpawner<PurchaseWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledPurchaseWalkerSpawner : PooledWalkerSpawner<PurchaseWalker> { }
}