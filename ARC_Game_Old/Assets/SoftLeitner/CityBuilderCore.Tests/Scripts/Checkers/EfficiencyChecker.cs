using NUnit.Framework;

namespace CityBuilderCore.Tests
{
    public class EfficiencyChecker : CheckerBase
    {
        public Building Building;
        public bool ExpectedEfficiency;
        public bool ActualEfficiency => Building.IsWorking;

        public override void Check()
        {
            Assert.AreEqual(ExpectedEfficiency, ActualEfficiency, name);
        }
    }
}