using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// receives a specific item and uses it up over time<br/>
    /// </summary>
    [Serializable]
    public class ItemRecipient
    {
        [Tooltip("the item used in this recipient")]
        public Item Item;
        [Tooltip("storage used by the recipient, determines how many items it can store")]
        public ItemStorage Storage;
        [Tooltip("time in seconds between using up 1 of the stored items")]
        public float ConsumptionInterval;

        public bool HasAccess => Storage.HasItem(Item);
        public int Quantity => Storage.GetItemQuantity(Item);

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

            Storage.RemoveItems(Item, 1);

            if (!HasAccess)
                Lost?.Invoke();
        }

        public void Fill(ItemStorage storage)
        {
            var hadAccess = HasAccess;

            storage.MoveItemsTo(Storage, Item);

            if (!hadAccess && HasAccess)
                Gained?.Invoke();
        }

        public void Modify(int quantity)
        {
            var hadAccess = HasAccess;

            if (quantity > 0)
                Storage.AddItems(Item, quantity);
            else
                Storage.RemoveItems(Item, quantity);

            if (!hadAccess && HasAccess)
                Gained?.Invoke();
        }

        #region Saving
        [Serializable]
        public class ItemsRecipientData
        {
            public string Key;
            public float CurrentTime;
            public ItemStorage.ItemStorageData Storage;
        }

        public ItemsRecipientData SaveData() => new ItemsRecipientData() { Key = Item.Key, CurrentTime = _currentTime, Storage = Storage.SaveData() };
        public void LoadData(ItemsRecipientData data)
        {
            _currentTime = data.CurrentTime;
            Storage.LoadData(data.Storage);
        }
        #endregion
    }
}