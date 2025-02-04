namespace CityBuilderCore.Tests
{
    public class ProductionTesting : TestingBase
    {
        public override string ScenePath => @"Assets/SoftLeitner/CityBuilderCore.Tests/City/ResourceSystems/Produce/ProductionDebugging.unity";
        public override float TimeScale => 10f;
        public override float Delay => 20f;
    }
}