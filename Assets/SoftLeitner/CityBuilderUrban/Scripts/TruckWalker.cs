using CityBuilderCore;
using System;
using UnityEngine;

namespace CityBuilderUrban
{
    /// <summary>
    /// randomly roams around and supplies and shop it encounters
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/urban">https://citybuilder.softleitner.com/manual/urban</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_urban_1_1_truck_walker.html")]
    public class TruckWalker : BuildingComponentWalker<ShopComponent>
    {
        public ItemQuantity Items;

        protected override void onComponentEntered(ShopComponent shop)
        {
            base.onComponentEntered(shop);

            shop.Supply(Items);
        }
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualTruckWalkerSpawner : ManualWalkerSpawner<TruckWalker> { }
}
