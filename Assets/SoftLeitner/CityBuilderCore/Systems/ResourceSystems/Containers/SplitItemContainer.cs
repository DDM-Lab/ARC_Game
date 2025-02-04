using System;
using System.Collections.Generic;
using System.Linq;

namespace CityBuilderCore
{
    /// <summary>
    /// item container that combines different <see cref="ItemStorage"/>s that are each meant for one specific item<br/>
    /// used in the <see cref="ProductionComponent"/> where storage is split between different producers and consumers
    /// </summary>
    public class SplitItemContainer : IItemContainer
    {
        private IEnumerable<ItemStorage> _storages;
        private Func<Item, ItemStorage> _itemStorageGetter;

        public SplitItemContainer(IEnumerable<ItemStorage> storages, Func<Item, ItemStorage> itemStorageGetter)
        {
            _storages = storages;
            _itemStorageGetter = itemStorageGetter;
        }

        public void ReserveCapacity(Item item, int amount) => _itemStorageGetter(item).ReserveCapacity(item, amount);
        public void UnreserveCapacity(Item item, int amount) => _itemStorageGetter(item).UnreserveCapacity(item, amount);

        public void ReserveQuantity(Item item, int amount) => _itemStorageGetter(item).ReserveQuantity(item, amount);
        public void UnreserveQuantity(Item item, int amount) => _itemStorageGetter(item).UnreserveQuantity(item, amount);

        public IEnumerable<Item> GetItems() => _storages.SelectMany(s => s.GetItems()).Distinct();
        public IEnumerable<ItemQuantity> GetItemQuantities() => GetItems().Select(i => new ItemQuantity(i, GetItemQuantity()));

        public int GetItemQuantity() => _storages.Sum(s => s.GetItemQuantity());
        public int GetItemQuantity(Item item) => _itemStorageGetter(item)?.GetItemQuantity(item) ?? 0;
        public int GetItemQuantity(ItemCategory itemCategory) => itemCategory.Items.Sum(GetItemQuantity);

        public int GetItemCapacity() => _storages.Sum(s => s.GetItemCapacity());
        public int GetItemCapacity(Item item) => _itemStorageGetter(item)?.GetItemCapacity(item) ?? 0;
        public int GetItemCapacity(ItemCategory itemCategory) => itemCategory.Items.Sum(GetItemCapacity);

        public int GetItemCapacityRemaining() => _storages.Sum(s => s.GetItemCapacityRemaining());
        public int GetItemCapacityRemaining(Item item) => _itemStorageGetter(item)?.GetItemCapacityRemaining(item) ?? 0;
        public int GetItemCapacityRemaining(ItemCategory itemCategory) => itemCategory.Items.Sum(GetItemCapacityRemaining);

        public int AddItems(Item item, int quantity) => _itemStorageGetter(item).AddItems(item, quantity);
        public int RemoveItems(Item item, int quantity) => _itemStorageGetter(item).RemoveItems(item, quantity);
    }
}