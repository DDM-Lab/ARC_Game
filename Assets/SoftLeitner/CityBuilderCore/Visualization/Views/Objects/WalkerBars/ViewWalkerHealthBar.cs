using UnityEngine;

namespace CityBuilderCore
{
    [CreateAssetMenu(menuName = "CityBuilder/Views/" + nameof(ViewWalkerHealthBar))]
    public class ViewWalkerHealthBar : ViewWalkerBarBase, IWalkerValue
    {
        public override IWalkerValue WalkerValue => this;

        public bool HasValue(Walker walker) => walker is IHealther;
        public float GetMaximum(Walker walker) => ((IHealther)walker).TotalHealth;
        public float GetValue(Walker walker) => ((IHealther)walker).CurrentHealth;
        public Vector3 GetPosition(Walker walker) => ((IHealther)walker).HealthPosition;
    }
}