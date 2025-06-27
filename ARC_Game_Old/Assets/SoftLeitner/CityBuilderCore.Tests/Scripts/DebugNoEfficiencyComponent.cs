namespace CityBuilderCore.Tests
{
    public class DebugNoEfficiencyComponent : BuildingComponent, IEfficiencyFactor
    {
        public override string Key => "DBG";

        public bool IsWorking => false;
        public float Factor => 0f;
    }
}