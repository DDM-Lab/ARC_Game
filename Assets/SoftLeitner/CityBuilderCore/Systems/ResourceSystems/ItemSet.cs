using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// some collection of items<br/>
    /// a set of all items in the game is needed by <see cref="ObjectRepository"/> so items can be found when a game gets loaded
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_item_set.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Sets/" + nameof(ItemSet))]
    public class ItemSet : KeyedSet<Item>, IBuildingValue, IWalkerValue
    {
        public bool HasValue(IBuilding building) => building.GetBuildingComponents<IBuildingComponent>().OfType<IItemOwner>().Any(h => h.ItemContainer.GetItemCapacity() > 0);
        public float GetMaximum(IBuilding building) => building.GetBuildingComponents<IBuildingComponent>().OfType<IItemOwner>().Sum(h => h.ItemContainer.GetItemCapacity());
        public float GetValue(IBuilding building) => building.GetBuildingComponents<IBuildingComponent>().OfType<IItemOwner>().Sum(h => h.ItemContainer.GetItemQuantity());
        public Vector3 GetPosition(IBuilding building) => building.WorldCenter;

        public bool HasValue(Walker walker) => walker.ItemStorage != null;
        public float GetMaximum(Walker walker) => walker.ItemStorage.GetItemCapacity();
        public float GetValue(Walker walker) => walker.ItemStorage.GetItemQuantity();
        public Vector3 GetPosition(Walker walker) => walker.Pivot.position;
    }
}