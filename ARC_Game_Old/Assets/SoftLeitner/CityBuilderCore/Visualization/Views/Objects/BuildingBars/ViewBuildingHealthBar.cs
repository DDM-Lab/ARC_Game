using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    [CreateAssetMenu(menuName = "CityBuilder/Views/" + nameof(ViewBuildingHealthBar))]
    public class ViewBuildingHealthBar : ViewBuildingBarBase, IBuildingValue
    {
        public override IBuildingValue BuildingValue => this;

        public bool HasValue(IBuilding building) => building.HasBuildingPart<IHealther>();
        public float GetMaximum(IBuilding building) => building.GetBuildingParts<IHealther>().Sum(h => h.TotalHealth);
        public float GetValue(IBuilding building) => building.GetBuildingParts<IHealther>().Sum(h => h.CurrentHealth);
        public Vector3 GetPosition(IBuilding building) => building.GetBuildingParts<IHealther>().First().HealthPosition;
    }
}