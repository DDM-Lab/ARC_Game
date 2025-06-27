using NUnit.Framework;
using System.Linq;

namespace CityBuilderCore.Tests
{
    public class BuildingPointChecker : CheckerBase
    {
        public BuildingInfo ExpectedBuilding;

        public override void Check()
        {
            Assert.AreEqual(ExpectedBuilding.Name, Dependencies.Get<IBuildingManager>().GetBuilding(Dependencies.Get<IGridPositions>().GetGridPoint(transform.position)).FirstOrDefault()?.Info.Name, name);
        }
    }
}