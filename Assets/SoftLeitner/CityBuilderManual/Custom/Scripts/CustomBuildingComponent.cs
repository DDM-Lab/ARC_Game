using CityBuilderCore;
using System;
using UnityEngine;

namespace CityBuilderManual.Custom
{
    public class CustomBuildingComponent : BuildingComponent, ICustomBuildingComponent, IEfficiencyFactor
    {
        public override string Key => "CCO";

        public bool IsWorking => _time > 0;
        public float Factor => _time > 0 ? 1 : 0;

        public GameObject GoodVisual;
        public GameObject BadVisual;

        public float Duration;

        private float _time;

        private void Update()
        {
            if (_time > 0)
            {
                _time -= Time.deltaTime;
            }

            GoodVisual.SetActive(IsWorking);
            BadVisual.SetActive(!IsWorking);
        }

        public void DoSomething()
        {
            _time = Duration;
        }

        #region Saving
        [Serializable]
        public class CustomComponentData
        {
            public float Time;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new CustomComponentData()
            {
                Time = _time
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<CustomComponentData>(json);

            _time = data.Time;
        }
        #endregion
    }
}
