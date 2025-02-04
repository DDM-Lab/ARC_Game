namespace CityBuilderCore.Tests
{
    public class RiskTesting : TestingBase
    {
        public override string ScenePath => @"Assets/SoftLeitner/CityBuilderCore.Tests/City/Risks/RiskDebugging.unity";
        public override float TimeScale => 10f;
        public override float Delay => 20f;
    }
}