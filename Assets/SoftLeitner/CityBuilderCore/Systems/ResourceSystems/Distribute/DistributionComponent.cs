using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that uses <see cref="SaleWalker"/>s to check what <see cref="IItemRecipient"/>s need<br/>
    /// it then gets them from <see cref="IItemGiver"/>s using <see cref="PurchaseWalker"/>s <br/>
    /// finally the <see cref="SaleWalker"/>s distributes the items to <see cref="IItemRecipient"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_distribution_component.html")]
    public class DistributionComponent : BuildingComponent, IDistributionComponent
    {
        public override string Key => "DST";

        [Tooltip("storage the distributor uses to store items arriving from givers before they are distributed to recipients")]
        public ItemStorage Storage;
        [Tooltip("fill out orders to limit the kinds of items the distributor carries and allow users to control the ratios")]
        public DistributionOrder[] Orders;

        [Tooltip("the walkers that get the items from the item givers")]
        public ManualPurchaseWalkerSpawner PurchaseWalkers;
        [Tooltip("check what recipients need and distribute it to them")]
        public CyclicSaleWalkerSpawner SaleWalkers;
        [Tooltip("minimum amount of item missing for a purchase walker to be sent")]
        public float MinimumOrder;

        ItemStorage IDistributionComponent.Storage => Storage;
        DistributionOrder[] IDistributionComponent.Orders => Orders;

        public IItemContainer ItemContainer => Storage;

        private List<Item> _wishlist = new List<Item>();

        private void Awake()
        {
            PurchaseWalkers.Initialize(Building, null, purchaserReturning);
            SaleWalkers.Initialize(Building, sellerLeaving, sellerReturning);
        }
        private void Start()
        {
            this.StartChecker(() => checkPurchase());
        }
        private void Update()
        {
            if (Building.IsWorking)
                SaleWalkers.Update();
        }

        public override string GetDebugText() => Storage.GetDebugText();

        public IEnumerable<Item> GetItems() => Storage.GetItems().Union(_wishlist);

        private IEnumerator checkPurchase()
        {
            if (!PurchaseWalkers.HasWalker)
                yield break;

            if (!Building.IsWorking)
                yield break;

            if (!Building.HasAccessPoint(PurchaseWalkers.Prefab.PathType, PurchaseWalkers.Prefab.PathTag))
                yield break;

            foreach (var item in _wishlist.OrderByDescending(i => Storage.GetUnitQuantity(i)))
            {
                var ratio = 1f;

                if (Orders != null && Orders.Length > 0)
                {
                    var order = Orders.FirstOrDefault(o => o.Item == item);
                    if (order == null || order.Ratio <= 0f)
                        continue;
                    ratio = order.Ratio;
                }

                var itemSpaceRemaining = Storage.GetItemCapacityRemaining(item, ratio);
                if (itemSpaceRemaining <= 0)
                    continue;

                var unitSpaceRemaining = Storage.GetUnitCapacityRemaining(item);
                if (unitSpaceRemaining < MinimumOrder)
                    continue;

                var items = new ItemQuantity(item, Math.Min(itemSpaceRemaining, PurchaseWalkers.Prefab.Storage.GetItemCapacityRemaining(item)));
                var query = Dependencies.Get<IGiverPathfinder>().GetGiverPathQuery(Building, null, items, PurchaseWalkers.Prefab.MaxDistance, PurchaseWalkers.Prefab.PathType, PurchaseWalkers.Prefab.PathTag);
                if (query == null)
                    continue;

                yield return new WaitForSeconds(1.0f);

                var path = query.Complete();
                if (path == null)
                    continue;

                PurchaseWalkers.Spawn(walker =>
                {
                    walker.StartPurchase(path, items);
                    Storage.ReserveCapacity(walker.Order.Item, walker.Order.Quantity);
                });
                yield break;
            }
        }

        private bool sellerLeaving(SaleWalker walker)
        {
            walker.StartSelling(Storage);
            return true;
        }
        private void sellerReturning(SaleWalker walker)
        {
            _wishlist = walker.Wishlist.ToList();
            walker.FinishSelling(Storage);
        }

        private void purchaserReturning(PurchaseWalker walker)
        {
            Storage.UnreserveCapacity(walker.Order.Item, walker.Order.Quantity);
            walker.Storage.MoveItemsTo(Storage, true);
        }

        #region Saving
        [Serializable]
        public class DistributionData
        {
            public ItemStorage.ItemStorageData Storage;
            public ManualWalkerSpawnerData PurchaseWalkers;
            public CyclicWalkerSpawnerData SaleWalkers;
            public string[] Wishlist;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new DistributionData()
            {
                Storage = Storage.SaveData(),
                PurchaseWalkers = PurchaseWalkers.SaveData(),
                SaleWalkers = SaleWalkers.SaveData(),
                Wishlist = _wishlist.Select(w => w.Key).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<DistributionData>(json);
            var items = Dependencies.Get<IKeyedSet<Item>>();

            Storage.LoadData(data.Storage);
            PurchaseWalkers.LoadData(data.PurchaseWalkers);
            SaleWalkers.LoadData(data.SaleWalkers);

            _wishlist = data.Wishlist.Select(k => items.GetObject(k)).ToList();
        }
        #endregion
    }
}