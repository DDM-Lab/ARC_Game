using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// bundels of items for whenever instead of a specific item just a general type of item is needed<br/>
    /// eg people need food not just potatoes specifically
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_item_category.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(ItemCategory))]
    public class ItemCategory : KeyedObject, IBuildingValue, IWalkerValue
    {
        [Tooltip("name used when refering to one(Food, Luxury Good, ...)")]
        public string NameSingular;
        [Tooltip("name used when refering to multiples(5 Foods, 3 Luxury Goods, ...)")]
        public string NamePlural;
        [Tooltip("the items in this item category")]
        public Item[] Items;

        private HashSet<Item> _items;

        public bool Contains(Item item)
        {
            if (_items == null)
                _items = new HashSet<Item>(Items);
            return _items.Contains(item);
        }

        public string GetName(int quantity)
        {
            if (quantity > 1)
                return $"{quantity} {NamePlural}";
            else
                return NameSingular;
        }

        public bool HasValue(IBuilding building) => building.GetBuildingComponents<IBuildingComponent>().OfType<IItemOwner>().Any(h => h.ItemContainer.GetItemCapacity(this) > 0);
        public float GetMaximum(IBuilding building) => building.GetBuildingComponents<IBuildingComponent>().OfType<IItemOwner>().Sum(h => h.ItemContainer.GetItemCapacity(this));
        public float GetValue(IBuilding building) => building.GetBuildingComponents<IBuildingComponent>().OfType<IItemOwner>().Sum(h => h.ItemContainer.GetItemQuantity(this));
        public Vector3 GetPosition(IBuilding building) => building.WorldCenter;

        public bool HasValue(Walker walker) => walker.ItemStorage != null;
        public float GetMaximum(Walker walker) => walker.ItemStorage.GetItemCapacity(this);
        public float GetValue(Walker walker) => walker.ItemStorage.GetItemQuantity(this);
        public Vector3 GetPosition(Walker walker) => walker.Pivot.position;
    }
}