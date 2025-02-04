using NUnit.Framework;

namespace CityBuilderCore.Tests
{
    public class BuildingChecker : CheckerBase
    {
        public Building Building;
        public BuildingInfo ExpectedBuilding;
        public BuildingInfo ActualBuilding => _buildingReference.Instance.Info;

        private BuildingReference _buildingReference;

        private void Start()
        {
            this.Delay(1, () =>
            {
                _buildingReference = Building.BuildingReference;
                _buildingReference.Instance.GetBuildingComponent<IEvolution>()?.CheckEvolution();
            });
        }

        public override void Check()
        {
            Assert.AreEqual(ExpectedBuilding.Name, ActualBuilding.Name, name);
        }
    }
}