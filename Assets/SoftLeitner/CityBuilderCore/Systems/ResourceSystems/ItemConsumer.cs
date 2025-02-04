using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// simple container that can check for an item quantity and remove it from a storage<br/>
    /// eg raw materials in production components
    /// </summary>
    [Serializable]
    public class ItemConsumer
    {
        [Tooltip("the item and quantity that are checked and removed from storage")]
        public ItemQuantity Items;
        [Tooltip("stores the items until they are consumed")]
        public ItemStorage Storage;

        public ItemLevel ItemLevel => Storage.GetItemLevel(Items.Item);
        public bool HasItems => Storage.HasItemsRemaining(Items.Item, Items.Quantity);

        public void Consume()
        {
            Storage.RemoveItems(Items.Item, Items.Quantity);
        }
    }
}