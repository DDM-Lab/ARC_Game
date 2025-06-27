using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class DebugItemStorer : MonoBehaviour
    {
        public StorageComponent Target;
        public Building TargetBuilding;
        public ItemQuantity Items;

        private void Start()
        {
            this.Delay(5, () =>
             {
                 if (Target)
                     Target.Storage.AddItems(Items.Item, Items.Quantity);
                 if (TargetBuilding)
                     TargetBuilding.GetBuildingParts<IItemOwner>().ForEach(i => i.ItemContainer.AddItems(Items.Item, Items.Quantity));
             });
        }
    }
}