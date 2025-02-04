using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// simple container that can check if an item quantity fits and adds it to a storage<br/>
    /// eg finished goods in production components
    /// </summary>
    [Serializable]
    public class ItemProducer
    {
        [Tooltip("the items that will be produced everytime a production 'happens'")]
        public ItemQuantity Items;
        [Tooltip("produced items are stored here until they are delivered somewhere else, when it is full production may seize")]
        public ItemStorage Storage;

        public ItemLevel ItemLevel => Storage.GetItemLevel(Items.Item);
        public bool FitsItems => Storage.FitsItems(Items.Item, Items.Quantity);
        public bool HasItem => Storage.HasItem(Items.Item);

        public void Produce()
        {
            Storage.AddItems(Items.Item, Items.Quantity);
        }
    }
}