using CityBuilderCore;

namespace CityBuilderManual.Custom
{
    public interface ICustomBuildingTrait : IBuildingTrait<ICustomBuildingTrait>
    {
        float CustomValue { get; }

        void DoSomething();
    }
}
