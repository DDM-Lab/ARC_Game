using NUnit.Framework;

namespace CityBuilderCore.Tests
{
    public class ItemRecipientChecker : CheckerBase
    {
        public EvolutionComponent EvolutionComponent;
        public Item Item;
        public int ExpectedQuantity;
        public int ActualQuantity => EvolutionComponent.ItemContainer.GetItemQuantity(Item);

        public override void Check()
        {
            Assert.AreEqual(ExpectedQuantity, ActualQuantity, name);
        }
    }
}