using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// an event that, on activation, adds or removes the specified items<br/>
    /// add > story based deliveries, discoveries, ...<br/>
    /// decrease > spoiled food, thieves, ...
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_item_storage_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(ItemStorageHappening))]
    public class ItemStorageHappening : TimingHappening
    {
        [Tooltip("whether the items will be added to global storage or the first storage components on the map")]
        public bool IsGlobal;
        [Tooltip("the items that will be added/removed(negative quantity to remove) when the happening triggers")]
        public ItemQuantity[] ItemQuantities;

        public override void Start()
        {
            base.Start();

            foreach (var itemQuantity in ItemQuantities)
            {
                var add = itemQuantity.Quantity > 0;
                var remaining = Mathf.Abs(itemQuantity.Quantity);

                if (IsGlobal)
                {
                    if (add)
                        Dependencies.Get<IGlobalStorage>().Items.AddItems(itemQuantity.Item, remaining);
                    else
                        Dependencies.Get<IGlobalStorage>().Items.RemoveItems(itemQuantity.Item, remaining);
                }
                else
                {
                    foreach (var storage in Dependencies.Get<IBuildingManager>().GetBuildings().SelectMany(b => b.GetBuildingComponents<IStorageComponent>()))
                    {
                        if (add)
                            remaining = storage.Storage.AddItems(itemQuantity.Item, remaining);
                        else
                            remaining = storage.Storage.RemoveItems(itemQuantity.Item, remaining);

                        if (remaining == 0)
                            break;
                    }
                }
            }
        }
    }
}
