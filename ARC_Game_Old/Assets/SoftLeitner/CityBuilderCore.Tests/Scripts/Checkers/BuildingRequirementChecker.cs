using NUnit.Framework;

namespace CityBuilderCore.Tests
{
    public class BuildingRequirementChecker : CheckerBase
    {
        public BuildingInfo BuildingInfo;
        public bool Expected;

        public override void Check()
        {
            Assert.AreEqual(Expected, BuildingInfo.CheckRequirements(Dependencies.Get<IGridPositions>().GetGridPoint(transform.position), BuildingRotation.Create()), name);
        }
    }
}