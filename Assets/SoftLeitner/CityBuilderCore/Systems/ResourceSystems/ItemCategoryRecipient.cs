using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// receives any item of a specified category and uses it up over time<br/>
    /// items are used in the order that they have in the category
    /// </summary>
    [Serializable]
    public class ItemCategoryRecipient
    {
        [Tooltip("the item category used in this recipient")]
        public ItemCategory ItemCategory;
        [Tooltip("storage used by the recipient, determines how many items it can store")]
        public ItemStorage Storage;
        [Tooltip("time in seconds between using up 1 of the stored items")]
        public float ConsumptionInterval;

        public bool HasAccess => ItemCategory.Items.Any(i => Storage.HasItem(i));

        public event Action Changed;
        public event Action Gained;
        public event Action Lost;

        private float _currentTime;

        public void Update(float multiplier)
        {
            if (!HasAccess)
                return;

            _currentTime += Time.deltaTime;

            if (_currentTime < ConsumptionInterval * multiplier)
                return;

            _currentTime = 0f;

            foreach (var item in ItemCategory.Items)
            {
                if (Storage.HasItem(item))
                {
                    Storage.RemoveItems(item, 1);
                    break;
                }
            }

            Changed?.Invoke();
            if (!HasAccess)
                Lost?.Invoke();
        }

        public void Fill(ItemStorage storage)
        {
            var hadAccess = HasAccess;

            foreach (var item in ItemCategory.Items)
            {
                storage.MoveItemsTo(Storage, item);
            }

            Changed?.Invoke();
            if (!hadAccess && HasAccess)
                Gained?.Invoke();
        }

        public void Modify(Item item, int quantity)
        {
            var hadAccess = HasAccess;

            if (quantity > 0)
                Storage.AddItems(item, quantity);
            else
                Storage.RemoveItems(item, quantity);

            Changed?.Invoke();
            if (!hadAccess && HasAccess)
                Gained?.Invoke();
            else if (hadAccess && !HasAccess)
                Lost?.Invoke();
        }

        #region Saving
        [Serializable]
        public class ItemsCategoryRecipientData
        {
            public string Key;
            public float CurrentTime;
            public ItemStorage.ItemStorageData Storage;
        }

        public ItemsCategoryRecipientData SaveData() => new ItemsCategoryRecipientData() { Key = ItemCategory.Key, CurrentTime = _currentTime, Storage = Storage.SaveData() };
        public void LoadData(ItemsCategoryRecipientData data)
        {
            _currentTime = data.CurrentTime;
            Storage.LoadData(data.Storage);
        }
        #endregion
    }
}