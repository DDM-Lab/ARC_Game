using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that periodically checks for attackers in a radius<br/>
    /// it can do damage or add addons to the attackers it finds
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/attacks">https://citybuilder.softleitner.com/manual/attacks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_defender_area_component.html")]
    public class DefenderAreaComponent : BuildingComponent
    {
        public override string Key => "DEF";

        [Tooltip("origin for the attack radius")]
        public Transform Origin;
        [Tooltip("visual is activated briefly when hurting the attacker")]
        public GameObject Visual;

        public float Radius = 10;
        [Tooltip("time to wait between checking for attackers")]
        public float Interval = 0.1f;
        [Tooltip("time to wait after an attacker was hurt")]
        public float Cooldown = 3f;
        [Tooltip("damage this defender does to attackers")]
        public int Damage = 0;
        public WalkerAddon Addon;

        private float _time;

        private void Update()
        {
            if (Time.deltaTime == 0f)
                return;

            _time -= Time.deltaTime * (Building == null ? 1f : Building.Efficiency);
            if (_time > 0)
                return;

            if (defend())
                _time = Cooldown;
            else
                _time = Interval;
        }

        private void OnDrawGizmos()
        {
            Gizmos.DrawWireSphere(Origin.position, Radius);
        }

        private bool defend()
        {
            var any = false;

            foreach (var attacker in Dependencies.Get<IAttackManager>().GetAttackers(Origin.position, Radius))
            {
                any = true;

                if (Damage > 0)
                    attacker.Hurt(Damage);
                if (Addon && attacker is Walker walker)
                    walker.AddAddon(Addon);
            }

            if (any)
            {
                Visual.SetActive(true);
                this.Delay(0.1f, () => Visual.SetActive(false));
            }

            return any;
        }

        #region Saving
        [Serializable]
        public class DefenderAreaData
        {
            public float Time;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new DefenderAreaData()
            {
                Time = _time
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<DefenderAreaData>(json);

            _time = data.Time;
        }
        #endregion
    }
}