using CityBuilderCore;
using System;
using UnityEngine;

namespace CityBuilderUrban
{
    /// <summary>
    /// can be spawned from a house to drive around randomly and purchase from any shops it encounters
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/urban">https://citybuilder.softleitner.com/manual/urban</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_urban_1_1_van_walker.html")]
    public class VanWalker : BuildingComponentWalker<ShopComponent>
    {
        public ItemQuantity Items;

        protected override void onComponentEntered(ShopComponent shop)
        {
            base.onComponentEntered(shop);

            shop.Purchase(Items);
        }
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicVanWalkerSpawner : CyclicWalkerSpawner<VanWalker> { }
}
