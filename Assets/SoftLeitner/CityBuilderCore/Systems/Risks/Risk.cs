using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for risks<br/>
    /// risks are special building values that execute when their value fills up completely<br/>
    /// for example fire risk that adds up and causes a fire over time unless preventative measures are provided
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/risks">https://citybuilder.softleitner.com/manual/risks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_risk.html")]
    public abstract class Risk : KeyedObject, IBuildingValue, IWalkerValue
    {
        [Tooltip("display name")]
        public string Name;
        [Tooltip("icon displayed in ui")]
        public Sprite Icon;
        [Tooltip("can be filled to show a notification when the risk executes, the first parameter {0} will be replaced with the buildings name")]
        public string ExecuteNotification;
        [Tooltip("can be filled to show a notification when the risk resolves, the first parameter {0} will be replaced with the buildings name")]
        public string ResolveNotification;

        [Tooltip("optional layer that can influence how fast a risk increases(eg heat for fire)")]
        public Layer MultiplierLayer;
        [Tooltip("value of the multiplier layer that will result in a multiplier of 0")]
        public float MultiplierLayerBottom;
        [Tooltip("value of the multiplier layer that will result in a multiplier of 2")]
        public float MultiplierLayerTop;

        public virtual void Execute(IRiskRecipient risker)
        {
            if (!string.IsNullOrWhiteSpace(ExecuteNotification))
                Dependencies.GetOptional<INotificationManager>()?.Notify(new NotificationRequest(string.Format(ExecuteNotification, risker.Building.GetName()), risker.Building.WorldCenter));
        }
        public virtual void Resolve(IRiskRecipient risker)
        {
            if (!string.IsNullOrWhiteSpace(ResolveNotification))
                Dependencies.GetOptional<INotificationManager>()?.Notify(new NotificationRequest(string.Format(ResolveNotification, risker.Building.GetName()), risker.Building.WorldCenter));
        }

        public bool HasValue(IBuilding building) => building?.HasBuildingComponent<IRiskRecipient>() ?? false;
        public float GetMaximum(IBuilding building) => 100f;
        public float GetValue(IBuilding building) => building?.GetBuildingComponent<IRiskRecipient>()?.GetRiskValue(this) ?? 0f;
        public Vector3 GetPosition(IBuilding building) => building.WorldCenter;

        public bool HasValue(Walker walker) => walker is RiskWalker riskWalker && riskWalker.Risk == this;
        public float GetMaximum(Walker _) => 1f;
        public float GetValue(Walker walker) => HasValue(walker) ? 1f : 0f;
        public Vector3 GetPosition(Walker walker) => walker.Pivot.position;

        public float GetMultiplier(IBuilding building)
        {
            if (MultiplierLayer == null)
                return 1f;

            float value = Dependencies.Get<ILayerManager>().GetValue(building.Point, MultiplierLayer) - MultiplierLayerBottom;
            return value / ((MultiplierLayerTop - MultiplierLayerBottom) / 2f);
        }

        public void ModifyValue(IBuilding building, float amount) => building?.GetBuildingComponent<IRiskRecipient>()?.ModifyRisk(this, amount);
    }
}