using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that periodically looks for an attacker in its range and hurts it<br/>
    /// that attack is visualized using a line renderer that show a line between defender and attacker
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/attacks">https://citybuilder.softleitner.com/manual/attacks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_defender_component.html")]
    public class DefenderComponent : BuildingComponent
    {
        public override string Key => "DEF";

        [Tooltip("origin for the attack-line")]
        public Transform Origin;
        [Tooltip("line renderer for the attack-line")]
        public LineRenderer LineRenderer;

        public float MaxDistance = 100;
        [Tooltip("time to wait between checking for attackers")]
        public float Interval = 0.1f;
        [Tooltip("time to wait after an attacker was hurt")]
        public float Cooldown = 1f;
        [Tooltip("damage this defender does to attackers")]
        public int Damage = 10;

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
            Gizmos.DrawWireSphere(Origin.position, MaxDistance);
        }

        private bool defend()
        {
            var attacker = Dependencies.Get<IAttackManager>().GetAttacker(Origin.position, MaxDistance);
            if (attacker == null)
                return false;

            LineRenderer.gameObject.SetActive(true);
            LineRenderer.SetPosition(0, Origin.position);
            LineRenderer.SetPosition(1, attacker.Position);

            this.Delay(0.1f, () => LineRenderer.gameObject.SetActive(false));

            attacker.Hurt(Damage);

            return true;
        }

        #region Saving
        [Serializable]
        public class DefenderData
        {
            public float Time;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new DefenderData()
            {
                Time = _time
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<DefenderData>(json);

            _time = data.Time;
        }
        #endregion
    }
}