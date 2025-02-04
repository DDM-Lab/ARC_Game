using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// walker addon that slows down the attack walker it is attached to<br/>
    /// the speed calculation itself is done in the attack walker<br/>
    /// the addon just carries the factor and removes itself after a set duration
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/attacks">https://citybuilder.softleitner.com/manual/attacks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_attack_slow_addon.html")]
    public class AttackSlowAddon : WalkerAddon
    {
        [Tooltip("how much the walker is slowed down(multiplier for speed)")]
        public float Factor = 0.5f;
        [Tooltip("time after which the addon removes itself[s]")]
        public float Duration = 2f;

        private float _progress;

        public override void Update()
        {
            base.Update();

            _progress += Time.deltaTime / Duration;
            if (_progress >= 1f)
                Walker.RemoveAddon(this);
        }

        public override void InitializeAddon()
        {
            base.InitializeAddon();

            Walker.GetComponentInChildren<Animator>().speed = Factor;
        }

        public override void TerminateAddon()
        {
            base.TerminateAddon();

            Walker.GetComponentInChildren<Animator>().speed = 1;
        }

        #region Saving
        [Serializable]
        public class SlowData
        {
            public float Progress;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new SlowData()
            {
                Progress = _progress
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<SlowData>(json);

            _progress = data.Progress;
        }
        #endregion
    }
}
