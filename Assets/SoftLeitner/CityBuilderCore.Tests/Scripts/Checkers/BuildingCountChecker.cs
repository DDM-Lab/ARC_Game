using NUnit.Framework;
using System.Linq;

namespace CityBuilderCore.Tests
{
    public class BuildingCountChecker : CheckerBase
    {
        public BuildingInfo BuildingInfo;
        public int ExpectedCount;

        public override void Check()
        {
            Assert.AreEqual(ExpectedCount, Dependencies.Get<IBuildingManager>().GetBuildings(BuildingInfo).Count(), name);
        }
    }
}