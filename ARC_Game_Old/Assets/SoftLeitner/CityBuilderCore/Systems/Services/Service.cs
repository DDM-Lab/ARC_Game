using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// services are special building values that are filled by walkers and decrease over time
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/services">https://citybuilder.softleitner.com/manual/services</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_service.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(Service))]
    public class Service : KeyedObject, IBuildingValue, IWalkerValue
    {
        [Tooltip("display name")]
        public string Name;
        [Tooltip("icon displayed in ui")]
        public Sprite Icon;

        [Tooltip("optional layer that can influence how fast a service decreases(eg heat for water)")]
        public Layer MultiplierLayer;
        [Tooltip("value of the multiplier layer that will result in a multiplier of 0")]
        public float MultiplierLayerBottom;
        [Tooltip("value of the multiplier layer that will result in a multiplier of 2")]
        public float MultiplierLayerTop;

        public bool HasValue(IBuilding building) => building?.HasBuildingComponent<IServiceRecipient>() ?? false;
        public float GetMaximum(IBuilding building) => 100f;
        public float GetValue(IBuilding building) => building?.GetBuildingComponent<IServiceRecipient>()?.GetServiceValue(this) ?? 0f;
        public Vector3 GetPosition(IBuilding building) => building.WorldCenter;

        public bool HasValue(Walker walker) => walker is ServiceWalker serviceWalker && serviceWalker.Service == this;
        public float GetMaximum(Walker _) => 1f;
        public float GetValue(Walker walker) => HasValue(walker) ? 1f : 0f;
        public Vector3 GetPosition(Walker walker) => walker.Pivot.position;

        public float GetMultiplier(IBuilding building)
        {
            if (MultiplierLayer == null)
                return 1f;

            float value = Dependencies.Get<ILayerManager>().GetValue(building.Point, MultiplierLayer) - MultiplierLayerBottom;
            return value / (MultiplierLayerTop - MultiplierLayerBottom);
        }

        public void ModifyValue(IBuilding building, float amount) => building?.GetBuildingComponent<IServiceRecipient>()?.ModifyService(this, amount);
    }
}