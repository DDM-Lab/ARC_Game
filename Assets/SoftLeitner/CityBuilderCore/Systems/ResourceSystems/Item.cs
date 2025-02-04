using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// scriptable object that describes the properties of an item
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_item.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(Item))]
    public class Item : KeyedObject, IBuildingValue, IWalkerValue
    {
        [Tooltip("display name")]
        public string Name;
        [Tooltip("priority of the item in logistics")]
        public int Priority;
        [Tooltip("how many items one (storage)unit stands for(eg storages capped to 1 unit can store 100 potatoes but only 1 block of granite)")]
        public int UnitSize = 1;
        [Tooltip("icon displayed in ui")]
        public Sprite Icon;
        [Tooltip("material used in visualizers")]
        public Material Material;
        [Tooltip("visuals used in visualizers")]
        public StorageQuantityVisual[] Visuals;

        public bool HasValue(IBuilding building) => building.GetBuildingParts<IItemOwner>().Any(h => h.ItemContainer.GetItemCapacity(this) > 0);
        public float GetMaximum(IBuilding building) => building.GetBuildingParts<IItemOwner>().Sum(h => h.ItemContainer.GetItemCapacity(this));
        public float GetValue(IBuilding building) => building.GetBuildingParts<IItemOwner>().Sum(h => h.ItemContainer.GetItemQuantity(this));
        public Vector3 GetPosition(IBuilding building) => building.WorldCenter;

        public bool HasValue(Walker walker) => walker.ItemStorage != null;
        public float GetMaximum(Walker walker) => walker.ItemStorage.GetItemCapacity(this);
        public float GetValue(Walker walker) => walker.ItemStorage.GetItemQuantity(this);
        public Vector3 GetPosition(Walker walker) => walker.Pivot.position;

        public int GetGlobalQuantity() => Dependencies.Get<IGlobalStorage>().Items.GetItemQuantity(this);
        public int GetStoredQuantity() => Dependencies.Get<IBuildingManager>().GetBuildingTraits<IStorageComponent>().Sum(s => s.ItemContainer.GetItemQuantity(this));
        public int GetBuildingOwnedQuantity() => Dependencies.Get<IBuildingManager>().GetBuildingParts<IItemOwner>().Sum(i => i.ItemContainer.GetItemQuantity(this));
        public int GetWalkerOwnedQuantity() => Dependencies.Get<IWalkerManager>().GetWalkers().OfType<IItemOwner>().Sum(i => i.ItemContainer.GetItemQuantity(this));
    }
}