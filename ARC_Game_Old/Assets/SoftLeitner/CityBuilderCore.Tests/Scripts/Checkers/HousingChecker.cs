using UnityEngine.Assertions;

namespace CityBuilderCore.Tests
{
    public class HousingChecker : CheckerBase
    {
        public HousingComponent HousingComponent;
        public Population Population;
        public int ExpectedQuantity;
        public int ActualQuantity => HousingComponent.GetQuantity(Population);

        public override void Check()
        {
            Assert.AreEqual(ExpectedQuantity, ActualQuantity, name);
        }
    }
}