using NUnit.Framework;

namespace CityBuilderCore.Tests
{
    public class StorageChecker : CheckerBase
    {
        public StorageComponent StorageComponent;
        public Item Item;
        public int ExpectedQuantity;
        public int ActualQuantity => StorageComponent.Storage.GetItemQuantity(Item);

        public override void Check()
        {
            Assert.AreEqual(ExpectedQuantity, ActualQuantity, name);
        }
    }
}