using NUnit.Framework;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class ItemOwnerChecker : CheckerBase
    {
        public Building Building;
        public Walker Walker;
        public Item Item;
        [Tooltip("-1 for full capacity")]
        public int ExpectedQuantity;

        public override void Check()
        {
            var actualQuantity = 0;
            var expectedQuantity = ExpectedQuantity;

            if (Building)
            {
                var container = Building.GetBuildingParts<IItemOwner>().First().ItemContainer;

                actualQuantity = container.GetItemQuantity(Item);

                if (expectedQuantity == -1)
                    expectedQuantity = container.GetItemCapacity(Item);
            }
            else if (Walker)
            {
                actualQuantity = Walker.ItemStorage.GetItemQuantity(Item);

                if (expectedQuantity == -1)
                    expectedQuantity = Walker.ItemStorage.GetItemCapacity(Item);
            }

            Assert.AreEqual(expectedQuantity, actualQuantity, name);
        }
    }
}