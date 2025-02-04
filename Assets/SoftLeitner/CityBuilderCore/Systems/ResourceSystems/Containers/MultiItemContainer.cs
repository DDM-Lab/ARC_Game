using System;
using System.Collections.Generic;
using System.Linq;

namespace CityBuilderCore
{
    /// <summary>
    /// item container that combines different <see cref="ItemStorage"/>s<br/>
    /// used in the <see cref="EvolutionComponent"/> in which storage is split among the different recipients
    /// </summary>
    public class MultiItemContainer : IItemContainer
    {
        private IEnumerable<ItemStorage> _storages;
        private Func<Item, IEnumerable<ItemStorage>> _itemStoragesGetter;
        private Func<ItemCategory, IEnumerable<ItemStorage>> _itemCategoryStoragesGetter;

        public MultiItemContainer(IEnumerable<ItemStorage> storages, Func<Item, IEnumerable<ItemStorage>> itemStoragesGetter, Func<ItemCategory, IEnumerable<ItemStorage>> itemCategoryStoragesGetter)
        {
            _storages = storages;
            _itemStoragesGetter = itemStoragesGetter;
            _itemCategoryStoragesGetter = itemCategoryStoragesGetter;
        }

        public void ReserveCapacity(Item item, int amount) => _itemStoragesGetter(item).FirstOrDefault()?.ReserveCapacity(item, amount);
        public void UnreserveCapacity(Item item, int amount) => _itemStoragesGetter(item).FirstOrDefault()?.UnreserveCapacity(item, amount);

        public void ReserveQuantity(Item item, int amount) => _itemStoragesGetter(item).FirstOrDefault()?.ReserveQuantity(item, amount);
        public void UnreserveQuantity(Item item, int amount) => _itemStoragesGetter(item).FirstOrDefault()?.UnreserveQuantity(item, amount);

        public IEnumerable<Item> GetItems() => _storages.SelectMany(s => s.GetItems()).Distinct();
        public IEnumerable<ItemQuantity> GetItemQuantities() => GetItems().Select(i => new ItemQuantity(i, GetItemQuantity(i)));

        public int GetItemQuantity() => _storages.Sum(s => s.GetItemQuantity());
        public int GetItemQuantity(Item item) => _itemStoragesGetter(item).Sum(s => s.GetItemQuantity(item));
        public int GetItemQuantity(ItemCategory itemCategory) => _itemCategoryStoragesGetter(itemCategory).Sum(s => s.GetItemQuantity(itemCategory));

        public int GetItemCapacity() => _storages.Sum(s => s.GetItemCapacity());
        public int GetItemCapacity(Item item) => _itemStoragesGetter(item).Sum(s => s.GetItemCapacity(item));
        public int GetItemCapacity(ItemCategory itemCategory) => _itemCategoryStoragesGetter(itemCategory).Sum(s => s.GetItemCapacity(itemCategory));

        public int GetItemCapacityRemaining() => _storages.Sum(s => s.GetItemCapacityRemaining());
        public int GetItemCapacityRemaining(Item item) => _itemStoragesGetter(item).Sum(s => s.GetItemCapacityRemaining(item));
        public int GetItemCapacityRemaining(ItemCategory itemCategory) => _itemCategoryStoragesGetter(itemCategory).Sum(s => s.GetItemCapacityRemaining(itemCategory));

        public int AddItems(Item item, int quantity)
        {
            foreach (var storage in _itemStoragesGetter(item))
            {
                quantity = storage.AddItems(item, quantity);
                if (quantity <= 0)
                    break;
            }

            return quantity;
        }
        public int RemoveItems(Item item, int quantity)
        {
            foreach (var storage in _itemStoragesGetter(item))
            {
                quantity = storage.RemoveItems(item, quantity);
                if (quantity <= 0)
                    break;
            }

            return quantity;
        }
    }
}