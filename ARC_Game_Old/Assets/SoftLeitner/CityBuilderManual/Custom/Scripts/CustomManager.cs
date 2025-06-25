using CityBuilderCore;
using System.Linq;
using UnityEngine;

namespace CityBuilderManual.Custom
{
    public class CustomManager : MonoBehaviour, ICustomManager
    {
        public float Multiplier;

        private void Awake()
        {
            Dependencies.Register<ICustomManager>(this);
        }

        public float GetTotalValue()
        {
            return Dependencies.Get<IBuildingManager>().GetBuildingTraits<ICustomBuildingTrait>().Sum(t => t.CustomValue) * Multiplier;
        }
        public BuildingComponentPath<ICustomBuildingTrait> GetPath(BuildingReference home, PathType pathType, object pathTag = null)
        {
            if (pathTag is null)
            {
                throw new System.ArgumentNullException(nameof(pathTag));
            }

            foreach (var other in Dependencies.Get<IBuildingManager>().GetBuildingTraits<ICustomBuildingTrait>())
            {
                if (other.Building == home.Instance)
                    continue;

                var path = PathHelper.FindPath(home.Instance, other.Building, pathType, tag);
                if (path == null)
                    continue;

                return new BuildingComponentPath<ICustomBuildingTrait>(other.Reference, path);
            }

            return null;
        }

        public void Add(BuildingComponentReference<ICustomBuildingTrait> trait)
        {
            Debug.Log("Custom Trait Added!");
        }
        public void Remove(BuildingComponentReference<ICustomBuildingTrait> trait)
        {
            Debug.Log("Custom Trait Removed");
        }
    }
}