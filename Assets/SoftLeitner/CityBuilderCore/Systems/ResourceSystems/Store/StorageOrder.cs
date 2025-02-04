using System;
using UnityEngine;

namespace CityBuilderCore
{
    [Serializable]
    public class StorageOrder
    {
        [Tooltip("the items this order pertains to")]
        public Item Item;
        [Tooltip(@"how the item is handled
Neutral	passively accept item up to ratio
Get	actively gets items up to ratio
Empty	actively gets rid of items down to the ratio")]
        public StorageOrderMode Mode;
        [Tooltip("how much of the available capacity the item can use from 0 to 1")]
        public float Ratio;

        #region Saving
        [Serializable]
        public class StorageOrderData
        {
            public string Item;
            public int Mode;
            public float Ratio;
        }

        public StorageOrderData SaveData()
        {
            return new StorageOrderData()
            {
                Item = Item.Key,
                Mode = (int)Mode,
                Ratio = Ratio
            };
        }
        public static StorageOrder FromData(StorageOrderData data)
        {
            if (data == null)
                return null;

            return new StorageOrder()
            {
                Item = data.Item == null ? null : Dependencies.Get<IKeyedSet<Item>>().GetObject(data.Item),
                Mode = (StorageOrderMode)data.Mode,
                Ratio = data.Ratio
            };
        }
        #endregion
    }
}