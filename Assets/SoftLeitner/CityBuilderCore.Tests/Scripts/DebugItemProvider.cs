using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class DebugItemProvider : MonoBehaviour
    {
        public ItemQuantity Items;

        private void Start()
        {
            this.Delay(5, () =>
             {
                 foreach (var recipient in GetComponentsInChildren<EvolutionComponent>())
                 {
                     foreach (var itemsRecipient in recipient.ItemsRecipients)
                     {
                         var storage = new ItemStorage() { Mode = ItemStorageMode.Free };
                         storage.AddItems(Items.Item, Items.Quantity);
                         itemsRecipient.Fill(storage);
                     }
                     foreach (var itemsCategoryRecipient in recipient.ItemsCategoryRecipients)
                     {
                         var storage = new ItemStorage() { Mode = ItemStorageMode.Free };
                         storage.AddItems(Items.Item, Items.Quantity);
                         itemsCategoryRecipient.Fill(storage);
                     }
                 }
             });
        }
    }
}