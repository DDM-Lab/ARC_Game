using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that has health and can be attacked, terminates the building when its health runs out
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/attacks">https://citybuilder.softleitner.com/manual/attacks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_attackable_component.html")]
    public class AttackableComponent : BuildingComponent, IAttackable, IHealther
    {
        public override string Key => "ATB";

        [Tooltip("target that the health visual ist displayed over, if empty the building center is used")]
        public Transform HealthTarget;
        [Tooltip("initial and maximum health")]
        public int MaxHealth;
        [Tooltip("visual to be displayed when the health runs out, optional")]
        public DemolishVisual DemolishVisual;

        public BuildingComponentReference<IAttackable> Reference { get; set; }

        public float TotalHealth => MaxHealth;
        public float CurrentHealth => _currentHealth;
        public Vector3 HealthPosition => HealthTarget ? HealthTarget.position : Building.WorldCenter;

        private int _currentHealth;

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Reference = registerTrait<IAttackable>(this);

            _currentHealth = MaxHealth;
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            replaceTrait(this, replacement.GetBuildingComponent<IAttackable>());
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<IAttackable>(this);
        }

        public override string GetDebugText() => $"{CurrentHealth}/{MaxHealth}";

        public void Attack(int damage)
        {
            _currentHealth -= damage;
            if (_currentHealth > 0)
                return;

            DemolishVisual.Create(DemolishVisual, Building);
            Building.Terminate();
        }

        #region Saving
        [Serializable]
        public class AttackebleData
        {
            public int Health;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new AttackebleData()
            {
                Health = _currentHealth
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<AttackebleData>(json);

            _currentHealth = data.Health;
        }
        #endregion
    }
}