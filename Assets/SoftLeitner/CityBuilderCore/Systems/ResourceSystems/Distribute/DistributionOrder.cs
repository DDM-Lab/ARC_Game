using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper for <see cref="DistributionComponent"/> that combines an item with a ratio
    /// </summary>
    [Serializable]
    public class DistributionOrder
    {
        [Tooltip("the item this order applies to")]
        public Item Item;
        [Tooltip("how much of the item to put on stock in relation to the storage capacity(0-1)")]
        public float Ratio;
    }
}