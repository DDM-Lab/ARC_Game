using CityBuilderCore;

namespace CityBuilderManual.Custom
{
    public interface ICustomManager
    {
        float GetTotalValue();
        BuildingComponentPath<ICustomBuildingTrait> GetPath(BuildingReference home, PathType pathType, object pathTag = null);

        void Add(BuildingComponentReference<ICustomBuildingTrait> trait);
        void Remove(BuildingComponentReference<ICustomBuildingTrait> trait);
    }
}
