using NUnit.Framework;

namespace CityBuilderCore.Tests
{
    public class AddonChecker : CheckerBase
    {
        public Building Building;
        public string AddonKey;
        public bool ExpectedExistence;
        public bool ActualExistence => _buildingReference.Instance.GetAddon<BuildingAddon>(AddonKey);

        private BuildingReference _buildingReference;

        private void Start()
        {
            this.Delay(1, () =>
            {
                _buildingReference = Building.BuildingReference;
            });
        }

        public override void Check()
        {
            Assert.AreEqual(ExpectedExistence, ActualExistence, name);
        }
    }
}