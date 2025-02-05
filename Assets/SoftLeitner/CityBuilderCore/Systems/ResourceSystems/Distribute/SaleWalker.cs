using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// roams around and takes note of what items the <see cref="IItemRecipient"/> it encounters need and supplies them if it carries the item
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_sale_walker.html")]
    public class SaleWalker : BuildingComponentWalker<IItemRecipient>, IItemOwner
    {
        /// <summary>
        /// storage the walker fills with items from the distributor before it start walking around
        /// </summary>
        public ItemStorage Storage;
        [Tooltip(@"reserves space for items the seller takes with it
otherwise the distributor might purchase additional items while the seller is out
which might overfill the storage when the seller comes back")]
        public bool ReserveCapacity;
        [Tooltip(@"whether the seller is allowed to overfill the distribution storage when it returns
this may happen if the purchase walker has filled the storage in the meantime
if ReserveCapacity is true this should never happen")]
        public bool Overfill;

        public override ItemStorage ItemStorage => Storage;
        public IItemContainer ItemContainer => Storage;
        public List<Item> Wishlist => _wishlist;

        private List<Item> _wishlist = new List<Item>();
        private List<ItemQuantity> _reserved;

        public void StartSelling(ItemStorage storage)
        {
            _wishlist.Clear();
            var moved = storage.MoveItemsTo(Storage);

            if (ReserveCapacity)
            {
                moved.ForEach(i => storage.ReserveCapacity(i.Item, i.Quantity));
                _reserved = moved;
            }
        }

        public void FinishSelling(ItemStorage other)
        {
            if (ReserveCapacity)
            {
                _reserved.ForEach(i => other.UnreserveCapacity(i.Item, i.Quantity));
                _reserved = null;
            }

            Storage.MoveItemsTo(other, Overfill);
        }

        protected override void onComponentEntered(IItemRecipient itemRecipient)
        {
            base.onComponentEntered(itemRecipient);

            itemRecipient.FillRecipient(ItemStorage);

            foreach (var item in itemRecipient.GetRecipientItems())
            {
                if (!_wishlist.Contains(item))
                    _wishlist.Add(item);
            }
        }

        public override string GetDebugText() => Storage.GetDebugText();

        #region Saving
        [Serializable]
        public class SaleWalkerData : RoamingWalkerData
        {
            public ItemStorage.ItemStorageData Storage;
            public string[] Wishlist;
            public ItemQuantity.ItemQuantityData[] Reserved;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new SaleWalkerData()
            {
                WalkerData = savewalkerData(),
                State = (int)_state,
                Storage = Storage.SaveData(),
                Wishlist = _wishlist.Select(w => w.Key).ToArray(),
                Reserved = _reserved?.Select(i => i.GetData()).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            base.LoadData(json);

            var data = JsonUtility.FromJson<SaleWalkerData>(json);
            var items = Dependencies.Get<IKeyedSet<Item>>();

            Storage.LoadData(data.Storage);

            _wishlist = data.Wishlist.Select(k => items.GetObject(k)).ToList();

            if (ReserveCapacity && data.Reserved != null)
                _reserved = data.Reserved.Select(i => i.GetItemQuantity()).ToList();
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualSaleWalkerSpawner : ManualWalkerSpawner<SaleWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicSaleWalkerSpawner : CyclicWalkerSpawner<SaleWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledSaleWalkerSpawner : PooledWalkerSpawner<SaleWalker> { }
}